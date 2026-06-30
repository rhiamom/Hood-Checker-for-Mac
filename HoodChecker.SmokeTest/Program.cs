/***************************************************************************
 *   Hood Checker for Mac — headless engine smoke test                     *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 ***************************************************************************/

using HoodChecker.Engine;

if (args.Length < 1)
{
    Console.WriteLine("Hood Checker for Mac — engine smoke test");
    Console.WriteLine("Usage: HoodChecker.SmokeTest <neighborhood.package> [hexType]");
    Console.WriteLine("  hexType defaults to 0x4E474248 (NGBH).");
    return;
}

string path = args[0];
uint type = args.Length > 1
    ? Convert.ToUInt32(args[1], 16)
    : 0x4E474248; // NGBH

int n = PackageProbe.CountResourcesOfType(path, type);
Console.WriteLine($"{path}");
Console.WriteLine($"  {n} resource(s) of type 0x{type:X8}");
