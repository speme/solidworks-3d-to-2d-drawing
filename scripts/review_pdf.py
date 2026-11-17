"""PDF vector-text audit and preview. A diagnostic, not manufacturing certification.

Usage: python scripts/review_pdf.py path/to/job.report.json
Requires PyMuPDF (pip install pymupdf).
"""
import argparse
import json
import math
import heapq
import re
from pathlib import Path
import fitz


def assign_candidates(candidates):
    """Maximum-cardinality, minimum-distance assignment; a PDF word can serve one dimension only."""
    word_ids = sorted({word for choices in candidates for _, word, _, _ in choices})
    words = {word: i for i, word in enumerate(word_ids)}
    n = len(candidates)
    sink = n + len(words) + 1
    graph = [[] for _ in range(sink + 1)]

    def edge(a, b, cost):
        graph[a].append([b, len(graph[b]), 1, cost])
        graph[b].append([a, len(graph[a]) - 1, 0, -cost])

    for i, choices in enumerate(candidates):
        edge(0, i + 1, 0)
        for distance, word, _, _ in choices:
            edge(i + 1, n + 1 + words[word], distance)
    for wi in words.values():
        edge(n + 1 + wi, sink, 0)
    potential = [0.0] * len(graph)
    while True:
        distances = [math.inf] * len(graph)
        parent = [None] * len(graph)
        distances[0] = 0
        queue = [(0.0, 0)]
        while queue:
            cost, vertex = heapq.heappop(queue)
            if cost > distances[vertex] + 1e-9:
                continue
            for ei, (target, _, capacity, weight) in enumerate(graph[vertex]):
                value = cost + weight + potential[vertex] - potential[target]
                if capacity and value < distances[target] - 1e-9:
                    distances[target] = value
                    parent[target] = (vertex, ei)
                    heapq.heappush(queue, (value, target))
        if parent[sink] is None:
            break
        for vertex, cost in enumerate(distances):
            if math.isfinite(cost):
                potential[vertex] += cost
        vertex = sink
        while vertex:
            origin, ei = parent[vertex]
            item = graph[origin][ei]
            item[2] -= 1
            graph[vertex][item[1]][2] += 1
            vertex = origin
    matched = {}
    for i, choices in enumerate(candidates):
        selected = {target - n - 1 for target, _, capacity, _ in graph[i + 1] if target > n and target < sink and capacity == 0}
        for choice in choices:
            if words[choice[1]] in selected:
                matched[i] = choice
                break
    return matched


def numeric_word(text):
    # Accept common diameter/radius/degree prefixes; do not extract arbitrary numbers from IDs or tolerances.
    value = re.fullmatch(r"(?:[1-9]\d*[xX×]\s*)?[Ø⌀ΦφRrCc]?[ ]*([-+]?\d+(?:[.,]\d+)?)[°]?", text.strip())
    return float(value.group(1).replace(",", ".")) if value else None


def review(report_path):
    report_path = Path(report_path).resolve()
    job = json.loads(report_path.read_text(encoding="utf-8-sig"))
    pdf_path = Path(job["Drawing"]).with_suffix(".pdf")
    result = {"pdf": str(pdf_path), "status": "review_required", "pages": [], "dimension_links": [], "thread_links": [], "chamfer_links": []}
    with fitz.open(pdf_path) as pdf:
        for index, page in enumerate(pdf):
            words = page.get_text("words")
            collisions = []
            # Strict area intersection; ignore touching boxes and adjacent glyphs in a single word.
            for i, a in enumerate(words):
                ra = fitz.Rect(a[:4])
                for b in words[i + 1:]:
                    rb = fitz.Rect(b[:4])
                    overlap = ra & rb
                    if not overlap.is_empty and overlap.get_area() > .25 * min(ra.get_area(), rb.get_area()):
                        collisions.append({"a": a[4], "b": b[4], "bbox": list(overlap)})
            links = []
            if index == 0:
                all_candidates = []
                for dim in job.get("Dimensions", []):
                    pos = dim.get("Position")
                    if not pos or len(pos) < 2:
                        links.append({"id": dim["FullName"], "view": dim["View"], "matched": False, "reason": "missing_position"})
                        all_candidates.append([])
                        continue
                    x, y = pos[0] * 1000 * 72 / 25.4, page.rect.height - pos[1] * 1000 * 72 / 25.4
                    expected = dim.get("ValueSI", 0) * (180 / math.pi if dim.get("Type") in (3, 16) else 1000) if dim.get('ValueVerified', True) else None
                    candidates = []
                    for wi, word in enumerate(words):
                        value = numeric_word(word[4])
                        if value is None:
                            continue
                        if dim.get('Type') == 10 and dim.get('ChamferTextStyle') == 4 and not word[4].upper().startswith('C'):
                            continue
                        if expected is None or abs(value - expected) > max(.005, abs(expected) * 1e-5):
                            continue
                        rect = fitz.Rect(word[:4])
                        distance = math.hypot((rect.x0 + rect.x1) / 2 - x, (rect.y0 + rect.y1) / 2 - y)
                        if distance < 15 * 72 / 25.4:
                            candidates.append((distance, wi, rect, word[4]))
                    candidates.sort(key=lambda c: c[0])
                    link = {"id": dim["FullName"], "view": dim["View"], "value_mm_or_deg": expected, "matched": False}
                    all_candidates.append(candidates)
                    links.append(link)
                dimension_count = len(links)
                angle_links = []
                for callout in job.get('ChamferCallouts', []):
                    if callout.get('TextStyle') == 4:
                        continue
                    pos = callout.get('Position') or []
                    candidates = []
                    if len(pos) >= 2:
                        x, y = pos[0] * 1000 * 72 / 25.4, page.rect.height - pos[1] * 1000 * 72 / 25.4
                        for wi, word in enumerate(words):
                            value = numeric_word(word[4])
                            if not word[4].endswith('°') or value is None or abs(value-callout['AngleDegrees']) > .005:
                                continue
                            rect = fitz.Rect(word[:4])
                            distance = math.hypot((rect.x0+rect.x1)/2-x, (rect.y0+rect.y1)/2-y)
                            if distance < 15 * 72 / 25.4:
                                candidates.append((distance, wi, rect, word[4]))
                    link = {'id': callout['DimensionName'], 'view': callout['View'], 'angle_degrees': callout['AngleDegrees'], 'matched': False}
                    angle_links.append(link); all_candidates.append(candidates)
                for di, (distance, wi, rect, text) in assign_candidates(all_candidates).items():
                    entry = links[di] if di < dimension_count else angle_links[di-dimension_count]
                    entry.update({"matched": True, "text": text, "bbox": list(rect), "distance_pt": distance})
                result["dimension_links"] = links
                for callout in job.get('ChamferCallouts', []):
                    size = next((d for d in links if d['view']==callout['View'] and d['id'].split('@')[0]==callout['DimensionName']), {})
                    angle = next((d for d in angle_links if d['view']==callout['View'] and d['id']==callout['DimensionName']), {})
                    result['chamfer_links'].append({'id': callout['DimensionName'], 'view': callout['View'], 'text': callout['Text'], 'distance_matched': bool(size.get('matched')), 'angle_matched': callout.get('TextStyle')==4 or bool(angle.get('matched')), 'matched': bool(size.get('matched')) and (callout.get('TextStyle')==4 or bool(angle.get('matched')))})
                used_thread_rects = set()
                for callout in job.get("ThreadCallouts", []):
                    pos = callout.get("Position") or []
                    matches = []
                    if len(pos) >= 2 and callout.get("Text"):
                        x, y = pos[0] * 1000 * 72 / 25.4, page.rect.height - pos[1] * 1000 * 72 / 25.4
                        for rect in page.search_for(callout["Text"]):
                            identity = tuple(round(v, 3) for v in rect)
                            distance = math.hypot(max(rect.x0 - x, 0, x - rect.x1), max(rect.y0 - y, 0, y - rect.y1))
                            if identity not in used_thread_rects and distance < 15 * 72 / 25.4:
                                matches.append((distance, identity, rect))
                    entry = {"source_feature": callout["SourceFeature"], "view": callout["View"], "text": callout["Text"], "representation": callout["Representation"], "matched": bool(matches)}
                    if matches:
                        _, identity, rect = min(matches, key=lambda item: item[0])
                        used_thread_rects.add(identity); entry["bbox"] = list(rect)
                    result["thread_links"].append(entry)
            result["pages"].append({"page": index + 1, "word_count": len(words), "collisions": collisions})
            page.get_pixmap(matrix=fitz.Matrix(1.5, 1.5)).save(str(report_path.with_suffix("")) + f".page-{index+1}.png")
    result["all_dimensions_matched"] = bool(result["dimension_links"]) and all(x["matched"] for x in result["dimension_links"])
    result["all_thread_callouts_matched"] = all(x["matched"] for x in result["thread_links"])
    result["all_chamfer_callouts_matched"] = all(x["matched"] for x in result["chamfer_links"])
    # Hole coverage, dimension chains, leaders and tolerances remain engineering review tasks.
    target = report_path.with_suffix(".pdf-review.json")
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"audit": str(target), "dimensions_matched": sum(x["matched"] for x in result["dimension_links"]), "dimension_count": len(result["dimension_links"]), "collisions": sum(len(p["collisions"]) for p in result["pages"]), "status": result["status"]}, ensure_ascii=False))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    review(parser.parse_args().report)
