using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    public sealed class TemplateProfile
    {
        public string SchemaVersion { get; set; }
        public string Name { get; set; }
        public Dictionary<string,object> Options { get; set; }
        public TemplateProfile() { SchemaVersion="1.0"; Options=new Dictionary<string,object>(); }
    }
    public static class TemplateProfiles
    {
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024 }; }
        internal static void KnownKeys(Dictionary<string,object> data,IEnumerable<string> keys,string context)
        {
            var names=new HashSet<string>(keys,StringComparer.Ordinal);
            foreach(string name in data.Keys) if(!names.Contains(name)) throw new ArgumentException(context+" 包含未知字段："+name+"（大小写敏感）。");
        }
        internal static void Merge(Dictionary<string,object> target,Dictionary<string,object> source)
        {
            foreach(var pair in source)
            {
                var child=pair.Value as Dictionary<string,object>; object existing;
                var parent=target.TryGetValue(pair.Key,out existing)?existing as Dictionary<string,object>:null;
                if(child!=null && parent!=null) Merge(parent,child); else target[pair.Key]=pair.Value;
            }
        }
        public static string ResolvePath(string value,string root)
        { return string.IsNullOrWhiteSpace(value)?value:Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(root,value)); }
        internal static void NormalizeOptions(Dictionary<string,object> patch,string root)
        {
            KnownKeys(patch,typeof(DrawingOptions).GetProperties().Select(p=>p.Name),"Options");
            object inference;
            if(patch.TryGetValue("GearInference",out inference) && inference is Dictionary<string,object>) KnownKeys((Dictionary<string,object>)inference,typeof(GearingInput).GetProperties().Select(p=>p.Name),"GearInference");
            foreach(string key in new[]{"Template","SheetFormat","OutputDirectory"})
            { object value; if(patch.TryGetValue(key,out value) && value!=null) patch[key]=ResolvePath(Convert.ToString(value),root); }
        }
        public static TemplateProfile Load(string path)
        {
            path=Path.GetFullPath(path); var json=Json(); var raw=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            if(raw==null) throw new ArgumentException("模板配置为空。");
            KnownKeys(raw,new[]{"SchemaVersion","Name","Options"},"模板配置");
            var profile=json.ConvertToType<TemplateProfile>(raw);
            if(profile.SchemaVersion!="1.0" || profile.Options==null) throw new ArgumentException("模板配置版本不支持或缺少 Options。");
            NormalizeOptions(profile.Options,Path.GetDirectoryName(path)); return profile;
        }
        public static DrawingOptions Resolve(string profilePath,Dictionary<string,object> overrides,string root)
        {
            var json=Json(); var merged=json.Deserialize<Dictionary<string,object>>(json.Serialize(new DrawingOptions()));
            if(!string.IsNullOrWhiteSpace(profilePath)) Merge(merged,Load(ResolvePath(profilePath,root)).Options);
            if(overrides!=null)
            {
                var patch=json.Deserialize<Dictionary<string,object>>(json.Serialize(overrides)); NormalizeOptions(patch,root); Merge(merged,patch);
            }
            var options=json.ConvertToType<DrawingOptions>(merged); options.Validate(); return options;
        }
        public static void Save(string path,string name,DrawingOptions options)
        {
            // A profile describes drawing rules. Output directories and active model configurations belong to jobs.
            var json=Json(); var values=json.Deserialize<Dictionary<string,object>>(json.Serialize(options));
            foreach(string key in new[]{"OutputDirectory","Configuration","AllConfigurations"}) values.Remove(key);
            foreach(string key in new[]{"Template","SheetFormat"})
                if(values[key]!=null && !string.IsNullOrWhiteSpace(Convert.ToString(values[key]))) values[key]=Path.GetFullPath(Convert.ToString(values[key]));
            string full=Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full,json.Serialize(new TemplateProfile { Name=name,Options=values }),System.Text.Encoding.UTF8);
        }
    }
}
