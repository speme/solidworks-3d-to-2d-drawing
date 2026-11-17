using System.Reflection;

namespace AutoDrawing
{
    public static class RuntimeInfo
    {
        public static string Version { get { return typeof(RuntimeInfo).Assembly.GetName().Version.ToString(3); } }
        public static string AssemblyPath { get { return typeof(RuntimeInfo).Assembly.Location; } }
        public static string ToJson()
        { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { Version=Version,AssemblyPath=AssemblyPath }); }
    }
}
