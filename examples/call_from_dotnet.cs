// Compile x64 .NET Framework 4.8; reference AutoDrawing.dll and SolidWorks.Interop.sldworks.dll.
using System;
using System.IO;
using System.Runtime.InteropServices;
using AutoDrawing;
using SolidWorks.Interop.sldworks;

class Example
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        string path = Path.GetFullPath(args[0]);
        var request = GenerationRequest.Load(path);
        var result = GenerationService.Generate(app, request, Path.GetDirectoryName(path), Console.Error.WriteLine);
        Console.WriteLine(result.ToJson());
        return result.ExitCode;
    }
}
