/***************************************************************************
 *   Hood Checker for Mac — engine scaffold                                *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   (http://Mootilda.ModTheSims.info)                                     *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *                                                                         *
 *   This program is free software; you can redistribute it and/or modify  *
 *   it under the terms of the GNU General Public License as published by  *
 *   the Free Software Foundation; either version 2 of the License, or     *
 *   (at your option) any later version.                                   *
 ***************************************************************************/

using SimPe.Interfaces.Files;
using SimPe.Packages;

namespace HoodChecker.Engine;

/// <summary>
/// Temporary scaffold probe. Confirms the vendored <c>Filetypes</c> DBPF
/// backend and the Hood-Checker shims (<c>File.LoadFromFile</c>,
/// <c>GeneratableFile.FindFiles</c>) are wired up and callable.
/// The ported R_* resource handlers will replace this.
/// </summary>
public static class PackageProbe
{
    /// <summary>Opens a neighborhood package and counts resources of a TGI type.</summary>
    public static int CountResourcesOfType(string packagePath, uint type)
    {
        GeneratableFile pkg = SimPe.Packages.File.LoadFromFile(packagePath);
        IPackedFileDescriptor[] hits = pkg.FindFiles(type);
        return hits.Length;
    }
}
