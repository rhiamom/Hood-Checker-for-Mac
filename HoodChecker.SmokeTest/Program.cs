/***************************************************************************
 *   Hood Checker for Mac — headless engine smoke test                     *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using LotExpander;

if (args.Length >= 1 && args[0] == "--list")
{
    Console.WriteLine("Neighborhoods folder: " + (SimsPaths.NeighborhoodsFolder ?? "(not found)"));
    foreach (NeighborhoodInfo nb in NeighborhoodCatalog.List())
        Console.WriteLine($"  {nb.Code}: {nb.Name}");
    return;
}

if (args.Length < 1)
{
    Console.WriteLine("Hood Checker for Mac — engine smoke test");
    Console.WriteLine("Usage: HoodChecker.SmokeTest <NeighborhoodMain.package> [--fix] [--all]");
    Console.WriteLine("  --fix  attempt to remove invalid references (writes a .bak first)");
    Console.WriteLine("  --all  show all memories, not just invalid ones");
    return;
}

string path = args[0];
bool fix = Array.IndexOf(args, "--fix") >= 0;
bool all = Array.IndexOf(args, "--all") >= 0;

var run = new HoodCheckRun();
List<string> report = run.Run(path, fix, all);

Console.WriteLine(Path.GetFileName(path) + (fix ? "  [FIX MODE]" : "  [check only]"));
Console.WriteLine(new string('-', 60));
if (report.Count == 0)
    Console.WriteLine("(no output)");
else
    foreach (string line in report)
        Console.WriteLine(line);
Console.WriteLine(new string('-', 60));
Console.WriteLine($"{report.Count} line(s); progress {run.Progress:P0}");
