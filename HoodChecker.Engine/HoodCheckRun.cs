/***************************************************************************
 *   Hood Checker for Mac                                                  *
 *   Hood Checker © 2006 Andi8104, © 2007-2011 Mootilda                    *
 *   (http://Mootilda.ModTheSims.info)                                     *
 *   macOS port © 2026 GramzeSweatshop (rhiamom@mac.com)                   *
 *   GPL v2 or later. See Licences/GPL-LICENSE.txt                         *
 *                                                                         *
 *   Headless orchestration extracted from Mootilda's PrimaryForm: opens a *
 *   neighborhood package, builds the Sim name/ID maps, then drives the    *
 *   R_FAMT / R_SREL / R_NGBH handlers. UI calls (Liste.Items.Add,         *
 *   Progress, MessageBox) become AddToList / progress counters / a        *
 *   pluggable graveyard policy so the engine runs without WinForms.       *
 ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SimPe.Interfaces.Files;
using SimPe.Packages;

namespace LotExpander
{
    public sealed class HoodCheckRun : IHoodCheckContext
    {
        // Highest Sims 2 version this tool knows how to handle.
        private const uint uVersionNumber = 17;
        private readonly string[] sVersionStrings =
        {
            /*  0 */ "The Sims 2",
            /*  1 */ "The Sims 2 University",
            /*  2 */ "The Sims 2 Nightlife",
            /*  3 */ "The Sims 2 Open For Business",
            /*  4 */ "The Sims 2 Family Fun Stuff",
            /*  5 */ "The Sims 2 Glamour Life Stuff",
            /*  6 */ "The Sims 2 Pets",
            /*  7 */ "The Sims 2 Seasons",
            /*  8 */ "The Sims 2 Celebration! Stuff",
            /*  9 */ "The Sims™ 2 H&M® Fashion Stuff",
            /* 10 */ "The Sims™ 2 Bon Voyage",
            /* 11 */ "The Sims™ 2 Teen Style Stuff",
            /* 12 */ "The Sims™ 2 Store",
            /* 13 */ "The Sims™ 2 FreeTime",
            /* 14 */ "The Sims 2 Kitchen and Bath Stuff",
            /* 15 */ "The Sims 2 Ikea Stuff",
            /* 16 */ "The Sims™ 2 Apartment Life",
            /* 17 */ "The Sims™ 2 Mansion and Garden Stuff",
        };

        private GeneratableFile _pkg;
        private readonly List<string> _results = new();
        private bool _fix;
        private bool _userFileErrors;
        private int _workload;
        private int _progress;

        private readonly Dictionary<uint, string> _simNameFromInstance = new();
        private readonly Dictionary<uint, uint> _simInstanceFromID = new();
        private readonly Dictionary<uint, string> _simNameFromID = new();
        private readonly Dictionary<uint, string> _npcNameFromID = new();

        /// <summary>When true, every memory is listed, not just invalid ones.</summary>
        public bool DisplayAllMemories { get; set; }

        /// <summary>Whether a Sim's relationship with itself is treated as valid.</summary>
        public bool SelfRelationsValid { get; set; }

        /// <summary>
        /// Graveyard policy invoked on the fix path. Defaults to a headless,
        /// non-destructive "keep invalid urnstones". A UI sets this to prompt.
        /// </summary>
        public Func<uint, List<R_DESC>, GraveyardChoice> GraveyardPolicy { get; set; }
            = (_, __) => GraveyardChoice.Keep();

        // ---- IHoodCheckContext ----
        public IPackageFile NBPack => _pkg;
        public Dictionary<uint, string> SimNameFromInstance => _simNameFromInstance;
        public Dictionary<uint, uint> SimInstanceFromID => _simInstanceFromID;
        public void AddToList(string s) => _results.Add(s);
        public void MadeProgress() => _progress++;
        public void IncreaseWorkload(int i) => _workload += i;
        public GraveyardChoice ResolveGraveyard(uint hoodId, List<R_DESC> potentialLots)
            => GraveyardPolicy(hoodId, potentialLots);

        /// <summary>Progress as a 0..1 fraction (0 when no work counted yet).</summary>
        public double Progress => _workload > 0 ? (double)_progress / _workload : 0.0;

        /// <summary>
        /// Check (and optionally fix) a neighborhood. Returns the report lines
        /// (the same text the original tool showed in its results list).
        /// </summary>
        public List<string> Run(string neighborhoodPackagePath, bool fix, bool showAllMemories)
        {
            _fix = fix;
            DisplayAllMemories = showAllMemories;
            _results.Clear();
            _userFileErrors = false;
            _workload = _progress = 0;
            InitializeNPCNameFromID();

            _pkg = SimPe.Packages.File.LoadFromFile(neighborhoodPackagePath);
            bool hoodChanged = false;
            try
            {
                if (!ValidSims2Version())
                {
                    AddToList("WARNING: This neighborhood was saved with a newer Sims 2 version "
                              + "than this tool knows how to handle. Results may be incomplete.");
                }

                FindAllSimIDsAndNames();

                if (FindAllValidSims() == 0)
                {
                    AddToList("ERROR: This neighborhood has no valid sims. Cannot continue.");
                    return new List<string>(_results);
                }

                if (_userFileErrors)
                    AddToList("");

                var ftRes = new R_FAMT(this);
                hoodChanged |= ftRes.CheckAllFamilyTies(_fix);

                AddToList("");
                var srRes = new R_SREL(this);
                hoodChanged |= srRes.CheckAllSimRelations(_fix);

                AddToList("");
                var nmRes = new R_NGBH(this);
                hoodChanged |= nmRes.CheckAllMemories(_fix);

                if (_fix && hoodChanged)
                    SavePackage(neighborhoodPackagePath);
            }
            finally
            {
                _pkg?.ForgetUpdate();
                _pkg?.Close();
                _pkg = null;
            }
            return new List<string>(_results);
        }

        private bool ValidSims2Version()
        {
            // VERS - Version. Absent in some packs -> assume handleable.
            IPackedFileDescriptor PFD = NBPack.FindFile(0xEBFEE342, 0, 0xFFFFFFFF, 1);
            if (null == PFD)
                return true;

            var Ver = new R_VERS(NBPack, PFD, sVersionStrings);
            return Ver.VersionNumber <= uVersionNumber;
        }

        private void FindAllSimIDsAndNames()
        {
            _simNameFromID.Clear();
            foreach (uint key in _npcNameFromID.Keys)
                _simNameFromID.Add(key, _npcNameFromID[key]);

            // Look for the Characters subfolder next to the neighborhood package.
            string sPath = Path.GetDirectoryName(NBPack.FileName);
            string sPrefix = Path.GetFileName(sPath);
            sPath += Path.DirectorySeparatorChar + "Characters" + Path.DirectorySeparatorChar;
            if (!Directory.Exists(sPath))
                return;

            string sUsers = sPrefix + "_User*.package";
            string[] UserFileNames = Directory.GetFiles(sPath, sUsers);
            IncreaseWorkload(UserFileNames.Length);
            for (int i = 0; i < UserFileNames.Length; i++)
            {
                FindSimIDAndName(UserFileNames[i]);
                MadeProgress();
            }
        }

        private void FindSimIDAndName(string UserFileName)
        {
            GeneratableFile UserFile = null;
            try
            {
                UserFile = SimPe.Packages.File.LoadFromFile(UserFileName);
                uint uSimID = FindSimID(UserFile);
                string sName = FindSimName(UserFile);
                AddSimToDictionary(uSimID, sName, UserFileName);
            }
            catch
            {
                // If we can't get the sim's name, just move on to the next file.
            }
            if (null != UserFile)
            {
                UserFile.ForgetUpdate();
                UserFile.Close();
            }
        }

        private uint FindSimID(GeneratableFile UserFile)
        {
            // OBJD - Object Data
            IPackedFileDescriptor[] OBJDArray = UserFile.FindFiles(0x4F424A44);
            if (0 == OBJDArray.Length)
                throw new FileNotFoundException("Cannot find OBJD - Object Data");
            IPackedFileDescriptor OBJD = OBJDArray[0];

            IPackedFile PF = UserFile.Read(OBJD);
            BinaryReader BR = SimPe.Helper.GetBinaryReader(PF.UncompressedData);
            BR.BaseStream.Seek(0x5C, SeekOrigin.Begin);
            uint uSimID = BR.ReadUInt32();

            Debug.Assert(0 != uSimID);
            return uSimID;
        }

        private string FindSimName(GeneratableFile UserFile)
        {
            // CTSS - Catalog Description
            IPackedFileDescriptor[] CTSSArray = UserFile.FindFiles(0x43545353);
            if (0 == CTSSArray.Length)
                throw new FileNotFoundException("Cannot find CTSS - Catalog Description");
            IPackedFileDescriptor CTSS = CTSSArray[0];

            var ResS = new R_STR(UserFile, CTSS);
            string sFirstName = ResS.FindString(0);
            string sFamilyName = ResS.FindString(2);
            return string.Format("{0} {1}", sFirstName, sFamilyName);
        }

        private void AddSimToDictionary(uint uSimID, string sName, string sUserFileName)
        {
            if (!_simNameFromID.ContainsKey(uSimID))
                _simNameFromID.Add(uSimID, sName);
            else if (_npcNameFromID.ContainsKey(uSimID))
            {
                // Special name for NPC?
                _simNameFromID.Remove(uSimID);
                _simNameFromID.Add(uSimID, sName);
            }
            else
            {
                // Duplicate SimID; keep first name that was found.
                if (!_userFileErrors)
                    AddToList("User Files:");
                _userFileErrors = true;
                AddToList(string.Format("  Duplicate SimID: 0x{0:X8} is both {1} and {2}",
                    uSimID, _simNameFromID[uSimID], sName));
            }
        }

        private int FindAllValidSims()
        {
            _simNameFromInstance.Clear();
            _simInstanceFromID.Clear();

            // SDSC - Sim Descriptions
            IPackedFileDescriptor[] SimDescriptions = NBPack.FindFiles(0xAACE2EFB);
            foreach (IPackedFileDescriptor SimDesc in SimDescriptions)
            {
                IPackedFile PF = NBPack.Read(SimDesc);
                BinaryReader BR = SimPe.Helper.GetBinaryReader(PF.UncompressedData);

                int iPos = SimIDPosition(BR);
                BR.BaseStream.Seek(iPos, SeekOrigin.Begin);
                uint uSimID = BR.ReadUInt32();

                string sName;
                if (_simNameFromID.ContainsKey(uSimID))
                {
                    sName = _simNameFromID[uSimID];
                    sName = string.Format("0x{0:X4} {1}", SimDesc.Instance, sName);
                    if (!_simInstanceFromID.ContainsKey(uSimID))
                        _simInstanceFromID.Add(uSimID, SimDesc.Instance);
                }
                else
                {
                    // If the name can't be found, identify the sim by SimID.
                    if (!_userFileErrors)
                        AddToList("User File Warnings:");
                    _userFileErrors = true;
                    sName = string.Format("0x{0:X4} SimID=0x{1:X8}", SimDesc.Instance, uSimID);
                    AddToList(string.Format("  Sim has no Character file: {0}", sName));
                }

                _simNameFromInstance.Add(SimDesc.Instance, sName);
            }
            return SimDescriptions.Length;
        }

        private int SimIDPosition(BinaryReader BR)
        {
            BR.BaseStream.Seek(0x04, SeekOrigin.Begin);
            int version = BR.ReadInt32();

            // Position of the SimID in the SDSC depends on the game version.
            int iPos = 0x162;           // Base game
            if (version >= 0x36)        // Apartment Life
                iPos = 0x1DC;
            else if (version >= 0x33)   // Freetime
                iPos = 0x1D6;
            else if (version >= 0x2e)   // Bon Voyage
                iPos = 0x1A6;
            else if (version >= 0x2d)   // Bon Voyage: Takemizu / Three Lakes
                iPos = 0x01A0;
            else if (version >= 0x2c)   // Pets & Seasons
                iPos = 0x19E;
            else if (version >= 0x2a)   // Open for Business
                iPos = 0x19C;
            else if (version >= 0x29)   // Nightlife
                iPos = 0x194;
            else if (version >= 0x22)   // University
                iPos = 0x174;
            return iPos;
        }

        private void SavePackage(string path)
        {
            // Back up the original, then write the rebuilt package.
            string bak = path + ".bak";
            if (!System.IO.File.Exists(bak))
                System.IO.File.Copy(path, bak);
            byte[] bytes = _pkg.Build().ToArray();
            System.IO.File.WriteAllBytes(path, bytes);
        }

        private void InitializeNPCNameFromID()
        {
            // From SimPE source + various neighborhoods (objects.package OBJD / NPC-*).
            _npcNameFromID.Clear();
            _npcNameFromID.Add(0x13269F2D, "Bigfoot (NPC)");
            _npcNameFromID.Add(0x31946C3B, "Bird (NPC)");
            _npcNameFromID.Add(0xF03AE97B, "Father Time (NPC)");
            _npcNameFromID.Add(0x341FB0E2, "Genie (NPC)");
            _npcNameFromID.Add(0xD55EF625, "Good Witch Cat (NPC)");
            _npcNameFromID.Add(0x84EC24A8, "Grim Reaper (NPC)");
            _npcNameFromID.Add(0x2D7EB2DC, "Hula Zombie (NPC)");
            _npcNameFromID.Add(0x724CD298, "Ideal Plantsim (NPC)");
            _npcNameFromID.Add(0x0F67E576, "Mrs. CrumpleBottom (NPC)");
            _npcNameFromID.Add(0x7250E297, "Penguin (NPC)");
            _npcNameFromID.Add(0x2E17B9FC, "Pollination Technician (NPC)");
            _npcNameFromID.Add(0x2C996F9C, "Remote Control Car (NPC)");
            _npcNameFromID.Add(0x50596292, "Robot (NPC)");
            _npcNameFromID.Add(0x745B11D1, "Rod Humble (NPC)");
            _npcNameFromID.Add(0xF036D5C3, "Santa Claus (NPC)");
            _npcNameFromID.Add(0x51BFB2CD, "Skunk (NPC)");
            _npcNameFromID.Add(0x71B85E0D, "Skunk Skin (NPC)");
            _npcNameFromID.Add(0xF51A5E5B, "Spectral Assistant (NPC)");
            _npcNameFromID.Add(0x0F83C946, "TemplateDog");
            _npcNameFromID.Add(0x4D9530C6, "Therapist (NPC)");
            _npcNameFromID.Add(0x7040237A, "Toddler New Year (NPC)");
            _npcNameFromID.Add(0xB38590EB, "Witch Doctor (NPC)");
        }
    }
}
