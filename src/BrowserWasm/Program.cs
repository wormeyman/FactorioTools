using System.Runtime.InteropServices.JavaScript;
using Knapcode.FactorioTools.Contract;

public class Program
{
    private static void Main(string[] args)
    {
    }
}

public partial class Interop
{
    [JSExport]
    public static string Plan(string requestJson) => PlanOrchestrator.PlanJson(requestJson);

    [JSExport]
    public static string Normalize(string requestJson) => PlanOrchestrator.NormalizeJson(requestJson);
}
