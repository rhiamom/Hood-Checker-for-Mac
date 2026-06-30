/***************************************************************************
 *   Copyright (C) 2011 by Mootilda                                        *
 *   http://Mootilda.ModTheSims.info                                       *
 *                                                                         *
 *   This program is free software; you can redistribute it and/or modify  *
 *   it under the terms of the GNU General Public License as published by  *
 *   the Free Software Foundation; either version 2 of the License, or     *
 *   (at your option) any later version.                                   *
 *                                                                         *
 *   This program is distributed in the hope that it will be useful,       *
 *   but WITHOUT ANY WARRANTY; without even the implied warranty of        *
 *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the         *
 *   GNU General Public License for more details.                          *
 *                                                                         *
 *   You should have received a copy of the GNU General Public License     *
 *   along with this program; if not, write to the                         *
 *   Free Software Foundation, Inc.,                                       *
 *   59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.             *
 ***************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using SimPe.Interfaces.Files;
using SimPe.Packages;

namespace LotExpander
{
    public class R_NGBH
    {
#if DEBUG
        private bool Test_PrintReferences = false;  // Enable (T) or disable (F) printing of user file number & SimID
#endif
        private bool Test_PrintLog = true;          // Enable (T) or disable (F) printing of all memories to a log
        private bool Test_KeepSimsMemory = true;    // Enable (T) or disable (F) keeping of all memories belonging to a sim

        // Memories are stored in 4 separate groups
        enum TS2Type { Lot = 0, Family = 1, Sim = 2, Hood = 3 };
        private string[] sTS2TypeString = { "Lot", "Family", "Sim", "Neighborhood" };
        private string[] sTS2TypePluralString = { "Lots", "Families", "Sims", "Neighborhood" };
        // Was sizeof(TS2Type) in the original (relied on the enum's int backing
        // being 4 bytes == its 4 members). Indexed by TS2Type (Lot..Hood).
        private Dictionary<uint, string>[] NameFromInstance = new Dictionary<uint, string>[4];

        // List of primary neighborhood and all subhoods
        public Dictionary<uint, string> HoodNameFromID = new Dictionary<uint, string>();

        // For each neighborhood, the list of possible graveyard lots
        public Dictionary<uint, List<R_DESC>> PotentialGraveyardLots = new Dictionary<uint, List<R_DESC>>();

        // For each neighborhood, the default graveyard lot
        private Dictionary<uint, R_DESC> GraveyardLots = new Dictionary<uint, R_DESC>();
        private uint uAllHoods = 0xFFFFFFFF;

        // List of valid Lot Instances in the neighborhood and all subhoods
        private Dictionary<uint, string> LotNameFromInstance = new Dictionary<uint, string>();
        private Dictionary<uint, R_DESC> LotDescFromInstance = new Dictionary<uint, R_DESC>();

        // List of valid Family Instances in the neighborhood
        private Dictionary<uint, string> FamilyNameFromInstance = new Dictionary<uint, string>();

        // List of known NPC families by instance
        private Dictionary<uint, string> NPCFamilyFromInstance = new Dictionary<uint, string>();

        // List of known NPC types by ID
        private Dictionary<uint, string> NPCTypeFromID = new Dictionary<uint, string>();

        // List of Memories which require the valid Owner as the Subject
        // Check that owner is valid, subject is valid, owner is subject
        private static Dictionary<uint, string> MemoryAboutSelf = new Dictionary<uint, string>();

        // List of Memories which require a valid sim as the Subject
        // Check that owner is valid, subject is valid
        private static Dictionary<uint, string> MemoryAboutSim = new Dictionary<uint, string>();

        // List of Memories which require a non-sim Subject
        // Check that owner is valid
        private static Dictionary<uint, string> MemoryAboutObject = new Dictionary<uint, string>();

        // Some memories seem to require either a non-sim Subject or the valid Owner as the Subject
        // I suspect that only one of these is correct, but we have no way to tell which one.
        // It might just vary by EP and both may be correct.
        // For now, better safe than sorry.
        private static Dictionary<uint, string> MemoryAboutUnknown = new Dictionary<uint, string>();

        // Memory Items which have an inventory number or known inventory name
        private static Dictionary<uint, string> InventoryItem = new Dictionary<uint, string>();

        // Memory Items which do not have the Memory flag set
        // Cannot perform any general checks, because there are an unknown number of data items
        // Can check data for specific known GUIDs.
        private static Dictionary<uint, string> ItemNotMemory = new Dictionary<uint, string>();

        private const uint MemoryBlockVersion_BaseGame = 0x6F;
        private const uint MemoryBlockVersion_University = 0x70;
        private const uint MemoryBlockVersion_Nightlife = 0xBE;
        private const uint MemoryBlockVersion_OpenForBusiness = 0xC2;
        private const uint MemoryBlockVersion_Seasons = 0xCB;

        StreamWriter fLog;

        private IHoodCheckContext fParent;
        private bool bRemoveUnknown = false;
        private bool bHoodFileChanged = false;
        private string sFixed = "";

        private const int iMaxGrid = 128;

        public R_NGBH(IHoodCheckContext fp)
        {
            fParent = fp;
            InitializeNPCFamilyFromInstance();
            InitializeNPCTypeFromID();
            InitializeMemoryNameFromID();

            NameFromInstance[(int)TS2Type.Lot] = LotNameFromInstance;
            NameFromInstance[(int)TS2Type.Family] = FamilyNameFromInstance;
            NameFromInstance[(int)TS2Type.Sim] = fParent.SimNameFromInstance;

            GraveyardLots.Clear();

            FindAllValidLots();
            fParent.MadeProgress();

            FindAllValidFamilies();
            fParent.MadeProgress();
        }


        // Find all valid lots in this neighborhood and all subhoods?  Downtowns and Suburbs only?
        // Match hood number from IDNO with lot number
        private void FindAllValidLots()
        {
            HoodNameFromID.Clear();
            PotentialGraveyardLots.Clear();
            LotNameFromInstance.Clear();
            LotDescFromInstance.Clear();

            // For the neighborhood and all subhoods
            string[] sSubHoods = Directory.GetFiles(Path.GetDirectoryName(fParent.NBPack.FileName), "*.package");
            for (int i = 0; i < sSubHoods.Length; i++)
            {
                GeneratableFile SHPack = SimPe.Packages.File.LoadFromFile(sSubHoods[i]);
                Debug.Print(sSubHoods[i]);

                // LTXT - Lot Descriptions
                IPackedFileDescriptor[] LotDescriptions = SHPack.FindFiles(0x0BF999E7);

                // Skip hidden and empty neighborhoods, like Pets and Weather (Seasons)
                if (LotDescriptions.Length == 0)
                    continue;

                List<R_DESC> LotList = new List<R_DESC>();

                uint uHoodID = GetHoodID(SHPack);
                string sHoodName = GetHoodName(SHPack);
#if DEBUG
                if (Test_PrintReferences)
                    // For debugging purposes, add HoodID to name.
                    sHoodName += string.Format(" (HoodID=0x{0:X4})", uHoodID);
#endif
                HoodNameFromID.Add(uHoodID, sHoodName);

                // For every lot within the neighborhood or subhood
                foreach (IPackedFileDescriptor LotDesc in LotDescriptions)
                {
                    R_DESC Lot = new R_DESC(SHPack, LotDesc);
                    uint uLotInst = Lot.Instance;
                    string sName = string.Format("LotNumber=0x{0:X4}", uLotInst);

                    uint uInst = uLotInst | 0x00008000;
                    IPackedFileDescriptor PDF = SHPack.FindFile(0x53545223, 0, 0xFFFFFFFF, uInst);
                    if (null == PDF)
                    {
                        sName = Lot.LotName;
                        if (null == sName || "" == sName)
                            Debug.Fail(string.Format("Cannot find name for lot number 0x{0:X4}", uLotInst));
                    }
                    else
                    {
                        R_STR ResS = new R_STR(SHPack, PDF);
                        sName = ResS.FindString(0);
                    }
                    sName = string.Format("0x{0:X4} {1}", uLotInst, sName);

                    Lot.HoodID = uHoodID;
                    if (Lot.LotType < 2)    // Residential or Community lots only
                        LotList.Add(Lot);

                    if (LotNameFromInstance.ContainsKey(uLotInst))
                        Debug.Fail(string.Format("Duplicate lot number 0x{0:X4}", uLotInst));
                    else
                    {
                        LotNameFromInstance.Add(uLotInst, sName);
                        LotDescFromInstance.Add(uLotInst, Lot);
                    }
                }
                PotentialGraveyardLots.Add(uHoodID, LotList);
            }
        }

        private uint GetHoodID(GeneratableFile SHPack)
        {
            // IDNO - IDNO record
            IPackedFileDescriptor[] IDNOArray = SHPack.FindFiles(0xAC8A7A2E);
            if (0 == IDNOArray.Length)
                throw new FileNotFoundException("Cannot find IDNO - ID Number");
            // There shouldn't be more than one, so use the first one
            Debug.Assert(1 == IDNOArray.Length);
            R_IDNO resIDNO = new R_IDNO(SHPack, IDNOArray[0]);
            return resIDNO.HoodID;
        }

        private string GetHoodName(GeneratableFile SHPack)
        {
            // CTSS - Catalog Description
            IPackedFileDescriptor[] CTSSArray = SHPack.FindFiles(0x43545353);
            if (0 == CTSSArray.Length)
                throw new FileNotFoundException("Cannot find CTSS - Catalog Description");
            // There shouldn't be more than one, so use the first one
            Debug.Assert(1 == CTSSArray.Length);
            R_STR resCTSS = new R_STR(SHPack, CTSSArray[0]);
            return resCTSS.FindString(0);
        }

        private void FindAllValidFamilies()
        {
            FamilyNameFromInstance.Clear();

            /* All known NPC families are considered valid, even with no FAMI record
            foreach (uint key in NPCFamilyFromInstance.Keys)
                FamilyNameFromInstance.Add(key, NPCFamilyFromInstance[key]);
             */

            // FAMI - Family Information
            IPackedFileDescriptor[] Families = fParent.NBPack.FindFiles(0x46414D49);
            foreach (IPackedFileDescriptor FamilyInfo in Families)
            {
                string sName = string.Format("FamilyID=0x{0:X4}", FamilyInfo.Instance);
                uint uInst = FamilyInfo.Instance;
                IPackedFileDescriptor PDF = fParent.NBPack.FindFile(0x53545223, 0, 0xFFFFFFFF, uInst);
                if (null == PDF)
                    Debug.Fail(string.Format("Cannot find name for family instance 0x{0:X4}", FamilyInfo.Instance));
                else
                {
                    R_STR ResS = new R_STR(fParent.NBPack, PDF);
                    sName = ResS.FindString(0);

                    // Add family name to lot name
                    R_FAMI resFAMI = new R_FAMI(fParent.NBPack, FamilyInfo);
                    if (null != resFAMI)
                    {
                        uint uLotInst = resFAMI.LotInstance;
                        if (LotDescFromInstance.ContainsKey(uLotInst))
                        {
                            R_DESC Lot = LotDescFromInstance[uLotInst];
                            Lot.FamilyName = sName;
                        }
                    }
                    sName = string.Format("0x{0:X4} {1}", uInst, sName);
                    if (NPCFamilyFromInstance.ContainsKey(uInst))
                        sName += " (" + NPCFamilyFromInstance[uInst] + ")";
                }

                if (!FamilyNameFromInstance.ContainsKey(FamilyInfo.Instance))
                    FamilyNameFromInstance.Add(FamilyInfo.Instance, sName);
                else if (NPCFamilyFromInstance.ContainsKey(FamilyInfo.Instance))
                {
                    // Keep standard names for families, rather than using silly generated names
                    // FamilyNameFromInstance.Remove(FamilyInfo.Instance);
                    // FamilyNameFromInstance.Add(FamilyInfo.Instance, sName);
                }
                else
                    Debug.Fail(string.Format("Duplicate family ID 0x{0:X4}", FamilyInfo.Instance));
            }
            // return Families.Length;
        }

        // ToDo: Do we need to check the memory records for any of the subhoods?
        public bool CheckAllMemories(bool bFix)
        {
            bRemoveUnknown = bFix;
            sFixed = (bRemoveUnknown) ? "Removed: " : "";

            if (Test_PrintLog)
            {
                string sFileName = Path.Combine(AppContext.BaseDirectory, "Log.txt");
                fLog = new StreamWriter(sFileName);
                fLog.AutoFlush = true;
            }

            fParent.AddToList("Memories:");

            // NGBH - Neighborhood Memoies
            IPackedFileDescriptor[] HoodMemories;
            HoodMemories = fParent.NBPack.FindFiles(0x4E474248);
            Debug.Assert(1 == HoodMemories.Length);
            foreach (IPackedFileDescriptor Memories in HoodMemories)
                CheckMemoriesRecord(Memories);

            if (Test_PrintLog)
                fLog.Close();
            return bHoodFileChanged;
        }

        bool bMemoryRecordChanged = false;

        private void CheckMemoriesRecord(IPackedFileDescriptor Memories)
        {
            bMemoryRecordChanged = false;

            IPackedFile PF = fParent.NBPack.Read(Memories);
            byte[] Data = PF.UncompressedData;
            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);
            byte[] DataNew = new byte[Data.Length];
            BinaryWriter BW = new BinaryWriter(new MemoryStream(DataNew));

            uint uBlockVersion = ProcessMemoryHeader(BR, BW);

            // Neighborhood
            if (Test_PrintLog)
            {
                fLog.WriteLine(sTS2TypePluralString[(int)TS2Type.Hood] + ":");
                fLog.WriteLine(string.Format("  Slot:"));
            }
            CheckMemorySlot(BR, BW, uBlockVersion, TS2Type.Hood);

            // (In order) Lots, Families, Sims
            for (int it = 0; it < 3; it++)
            {
                if (Test_PrintLog)
                {
                    fLog.WriteLine("");
                    fLog.WriteLine(sTS2TypePluralString[it] + ":");
                }
                // For now, do not remove invalid sim memories
                if (Test_KeepSimsMemory && ((TS2Type)it == TS2Type.Sim))
                    sFixed = "";
                int iSlotCount = BR.ReadInt32();
                int iSlotCountNew = 0;
                long iSlotCountIndex = BW.BaseStream.Position;
                BW.Write(iSlotCount);

                for (int i = 0; i < iSlotCount; i++)
                {
                    if (Test_PrintLog)
                        fLog.WriteLine(string.Format("  Slot: {0}", i));
                    if (CheckMemorySlot(BR, BW, uBlockVersion, (TS2Type)it))
                        iSlotCountNew++;
                }
                if (bRemoveUnknown)
                {
                    long iIndex = BW.BaseStream.Position;
                    if (iSlotCountNew != iSlotCount)
                    {
                        // Fix the number of memories in the array
                        BW.BaseStream.Position = iSlotCountIndex;
                        BW.Write(iSlotCountNew);
                        BW.BaseStream.Position = iIndex;
                    }
                }
            }
            for (long iPos = BR.BaseStream.Position; iPos < Data.Length; iPos++)    // 5 bytes
                BW.Write(BR.ReadByte());

            if (bRemoveUnknown)
            {
                long iIndex = BW.BaseStream.Position;
                if ((iIndex != Data.Length) || bMemoryRecordChanged)
                {
                    bHoodFileChanged = true;

                    // Truncate to new size and write record.
                    byte[] DataTruncate = new byte[iIndex];
                    Array.Copy(DataNew, DataTruncate, iIndex);

                    Data = DataTruncate;
                    Memories.SetUserData(Data, true);
                }
            }
        }

        private uint ProcessMemoryHeader(BinaryReader BR, BinaryWriter BW)
        {
            uint uBlockID = BR.ReadUInt32();
            Debug.Assert(uBlockID == 0x4E474248);
            BW.Write(uBlockID);

            uint uBlockVersion = BR.ReadUInt32();
            Debug.Assert(
                (uBlockVersion == MemoryBlockVersion_BaseGame)
             || (uBlockVersion == MemoryBlockVersion_University)
             || (uBlockVersion == MemoryBlockVersion_Nightlife)
             || (uBlockVersion == MemoryBlockVersion_OpenForBusiness)
             || (uBlockVersion == MemoryBlockVersion_Seasons)
            );
            // ToDo: Determine whether other versions are known and handled correctly
            BW.Write(uBlockVersion);

            // Unused
            uint uDummy = BR.ReadUInt32();
            BW.Write(uDummy);

            // Height
            uDummy = BR.ReadUInt32();
            Debug.Assert(iMaxGrid == uDummy);
            BW.Write(uDummy);

            // Width
            uDummy = BR.ReadUInt32();
            Debug.Assert(iMaxGrid == uDummy);
            BW.Write(uDummy);

            // Terrain Type
            int iStrLen = BR.ReadInt32();
            BW.Write(iStrLen);
            byte[] bString = BR.ReadBytes(iStrLen);
            string sTerrainType = SimPe.Helper.ToString(bString);
            Debug.Assert(("concrete" == sTerrainType.ToLower())
                      || ("desert" == sTerrainType.ToLower())
                      || ("dirt" == sTerrainType.ToLower())
                      || ("temperate" == sTerrainType.ToLower()));
            BW.Write(bString);

            bString = BR.ReadBytes(28);
            BW.Write(bString);

            return uBlockVersion;
        }

        private bool CheckMemorySlot(BinaryReader BR, BinaryWriter BW, uint uBlockVersion, TS2Type instType)
        {
            Dictionary<uint, string> dict = NameFromInstance[(int)instType];

            long iIndex = BW.BaseStream.Position;

            uint uInstance = BR.ReadUInt32();
            BW.Write(uInstance);
            bool bExists = (null == dict) ? true : dict.ContainsKey(uInstance);
            string sName = (null == dict) ? "" : (bExists) ? dict[uInstance] : "";
            if (!bExists)
                fParent.AddToList(string.Format("  {0}{1} does not exist: 0x{2:X4}",
                    (bRemoveUnknown) ? "Removed: " : "", sTS2TypeString[(int)instType], uInstance));

            uint uVersion = uBlockVersion;
            if (uBlockVersion >= MemoryBlockVersion_Nightlife)
            {
                uVersion = BR.ReadUInt32();
                Debug.Assert(
                    (uBlockVersion == MemoryBlockVersion_BaseGame)
                 || (uBlockVersion == MemoryBlockVersion_University)
                 || (uBlockVersion == MemoryBlockVersion_Nightlife)
                 || (uBlockVersion == MemoryBlockVersion_OpenForBusiness)
                 || (uBlockVersion == MemoryBlockVersion_Seasons)
                );
                BW.Write(uVersion);
            }

            // Special Simulator Tokens
            const string sIndentMemorySlot = "    ";
            if (Test_PrintLog)
            {
                fLog.WriteLine(sIndentMemorySlot + string.Format("Instance: 0x{0:X8} {1}", uInstance, sName));
                if (uBlockVersion >= MemoryBlockVersion_Nightlife)
                    fLog.WriteLine(sIndentMemorySlot + string.Format("Version: 0x{0:X8}", uVersion));
                fLog.WriteLine(sIndentMemorySlot + "Special Simulator:");
            }
            CheckMemoryArray(BR, BW, uBlockVersion, instType, uInstance);

            // Standard Memories
            if (Test_PrintLog)
                fLog.WriteLine(sIndentMemorySlot + "Standard:");
            CheckMemoryArray(BR, BW, uBlockVersion, instType, uInstance);

            // For now, do not remove invalid sim memories unless the sim doesn't exist.
            if (bExists && Test_KeepSimsMemory && (instType == TS2Type.Sim))
                return true;

            if (!bExists)
                BW.BaseStream.Position = iIndex;    // Remove this memory slot
            return bExists;
        }

        private void CheckMemoryArray(BinaryReader BR, BinaryWriter BW,
            uint uBlockVersion, TS2Type instType, uint uInstance)
        {
            int iMemoryCount = BR.ReadInt32();
            int iMemoryCountNew = 0;
            long iMemoryCountIndex = BW.BaseStream.Position;
            BW.Write(iMemoryCount);
            fParent.IncreaseWorkload(iMemoryCount);
            const string sIndentMemoryArray = "      ";
            if (Test_PrintLog)
                fLog.WriteLine(sIndentMemoryArray + string.Format("Item Count: {0}", iMemoryCount));

            for (int i = 0; i < iMemoryCount; i++)
            {
                if (Test_PrintLog)
                    fLog.WriteLine(sIndentMemoryArray + string.Format("Item: {0}", i));
                if (CheckMemoryItem(BR, BW, uBlockVersion, instType, uInstance))
                    iMemoryCountNew++;
                fParent.MadeProgress();
            }

            if (bRemoveUnknown)
            {
                long iIndex = BW.BaseStream.Position;
                if (iMemoryCountNew != iMemoryCount)
                {
                    // Fix the number of memories in the array
                    BW.BaseStream.Position = iMemoryCountIndex;
                    BW.Write(iMemoryCountNew);
                    BW.BaseStream.Position = iIndex;
                }
            }
        }

        // ToDo: make flags a bitfield, or a structure, rather than individual bool?
        private bool bMemoryAboutSelf;
        private bool bMemoryAboutSim;
        private bool bMemoryAboutObject;
        private bool bMemoryAboutUnknown;
        private string sMemoryDesc;
        private string sMemoryName;
        private ushort[] uMemoryData;
        private int iDataCount;

        private bool CheckMemoryItem(BinaryReader BR, BinaryWriter BW,
            uint uBlockVersion, TS2Type instType, uint uInstance)
        {
            sMemoryName = "";
            bool bValidMemory = false;
            bool bInventory = false;
            bool bItemNotMemory;
            int iInventoryNumber = 0;

            long iIndex = BW.BaseStream.Position;

            Dictionary<uint, string> dict = NameFromInstance[(int)instType];
            bool bExists = true;
            string sInstanceName = (null == dict) ? "Neighborhood" : string.Format("0x{0:X4}", uInstance);
            if ((null != dict) && (bExists = dict.ContainsKey(uInstance)))
                sInstanceName = dict[uInstance];
            if ((TS2Type.Lot == instType) || (TS2Type.Family == instType))
                sInstanceName = sTS2TypeString[(int)instType] + " " + sInstanceName;
            sMemoryDesc = sInstanceName + ": ";

            bool bMemoryFound = false;
            uint uMemoryGuid = BR.ReadUInt32();
            BW.Write(uMemoryGuid);

            // Find the memory name and type by looking up known memory GUIDs.
            if (bMemoryAboutSelf = MemoryAboutSelf.ContainsKey(uMemoryGuid))
                sMemoryName = MemoryAboutSelf[uMemoryGuid];
            bMemoryFound = bMemoryFound || bMemoryAboutSelf;

            if (bMemoryAboutSim = (bMemoryFound) ? false : MemoryAboutSim.ContainsKey(uMemoryGuid))
                sMemoryName = MemoryAboutSim[uMemoryGuid];
            bMemoryFound = bMemoryFound || bMemoryAboutSim;

            if (bMemoryAboutObject = (bMemoryFound) ? false : MemoryAboutObject.ContainsKey(uMemoryGuid))
                sMemoryName = MemoryAboutObject[uMemoryGuid];
            bMemoryFound = bMemoryFound || bMemoryAboutObject;

            if (bMemoryAboutUnknown = (bMemoryFound) ? false : MemoryAboutUnknown.ContainsKey(uMemoryGuid))
                sMemoryName = MemoryAboutUnknown[uMemoryGuid];
            bMemoryFound = bMemoryFound || bMemoryAboutUnknown;

            if (bInventory = (bMemoryFound) ? false : InventoryItem.ContainsKey(uMemoryGuid))
                sMemoryName = InventoryItem[uMemoryGuid];
            bMemoryFound = bMemoryFound || bInventory;

            if (bItemNotMemory = (bMemoryFound) ? false : ItemNotMemory.ContainsKey(uMemoryGuid))
                sMemoryName = ItemNotMemory[uMemoryGuid];
            bMemoryFound = bMemoryFound || bItemNotMemory;

            if (!bMemoryFound)
                sMemoryName = string.Format("Memory(0x{0:X8})", uMemoryGuid);
#if DEBUG
            else if (Test_PrintReferences)
                // For debugging purposes, add Memory ID to name.
                sMemoryName = string.Format("Memory(0x{0:X8}) ", uMemoryGuid) + sMemoryName;
#endif

            ushort uFlags1 = BR.ReadUInt16();
            BW.Write(uFlags1);
            bool bVisible = (uFlags1 & 0x0001) != 0;
            bool bMemory = (uFlags1 & 0x0002) != 0;
            bool bFlag1 = (uFlags1 & 0x0008) != 0;      // Inventory item: Fish?
            Debug.Assert(0 == (uFlags1 & 0xFFF4));

            if (!bVisible)
                sMemoryDesc += "[Invisible] ";

            ushort uFlags2 = 0;
            if (uBlockVersion >= MemoryBlockVersion_OpenForBusiness)
            {
                uFlags2 = BR.ReadUInt16();
                BW.Write(uFlags2);
                Debug.Assert(0 == uFlags2);
            }

            if (uBlockVersion >= MemoryBlockVersion_Nightlife)
            {
                iInventoryNumber = BR.ReadInt32();
                BW.Write(iInventoryNumber);
                if (0 != iInventoryNumber)
                    sMemoryDesc += string.Format("[Inventory: {0}] ", iInventoryNumber);
            }

            ushort uUnk = 0;
            if (uBlockVersion >= MemoryBlockVersion_Seasons)
            {
                uUnk = BR.ReadUInt16();
                BW.Write(uUnk);
            }

            iDataCount = BR.ReadInt32();

            #region LogMemoryItem
            const string sIndentMemoryItem = "        ";
            if (Test_PrintLog)
            {
                fLog.WriteLine(sIndentMemoryItem + string.Format("GUID: 0x{0:X8} {1}", uMemoryGuid, sMemoryName));
                fLog.WriteLine(sIndentMemoryItem + string.Format("Flags 1: 0x{0:X4}{1}{2}",
                    uFlags1, ((bVisible) ? " Visible" : ""), ((bMemory) ? " Memory" : "")));
                if (uBlockVersion >= MemoryBlockVersion_OpenForBusiness)
                    fLog.WriteLine(sIndentMemoryItem + string.Format("Flags 2: 0x{0:X4}", uFlags2));
                if (uBlockVersion >= MemoryBlockVersion_Nightlife)
                    fLog.WriteLine(sIndentMemoryItem + string.Format("Inventory: {0}", iInventoryNumber));
                if (uBlockVersion >= MemoryBlockVersion_Seasons)
                    fLog.WriteLine(sIndentMemoryItem + string.Format("Seasons WORD: 0x{0:X4}", uUnk));
                fLog.WriteLine(sIndentMemoryItem + string.Format("Data count: {0}", iDataCount));
            }
            #endregion // LogMemoryItem

            uMemoryData = new ushort[iDataCount];
            for (int i = 0; i < iDataCount; i++)
            {
                uMemoryData[i] = BR.ReadUInt16();
                // Wait to write data until we've had a chance to fix it.
                #region LogMemoryData
                if (Test_PrintLog)
                {
                    string sLotName = "";
                    string sFamilyName = "";
                    string sSimName = "";
                    string sGUIDName = "";
                    if (bMemoryAboutSelf || bMemoryAboutSim || bMemoryAboutObject || bMemoryAboutUnknown)
                    {
                        if ((4 == i) || (12 == i))
                        {
                            if (fParent.SimNameFromInstance.ContainsKey(uMemoryData[i]))
                                sSimName = fParent.SimNameFromInstance[uMemoryData[i]];
                        }
                        else if (6 == i)
                        {
                            uint uSubjectGuid = (uint)((uMemoryData[6] << 16) | uMemoryData[5]);
                            if (uMysterySimGuid == uSubjectGuid)
                                sGUIDName = "Mystery Sim";
                            else if (fParent.SimInstanceFromID.ContainsKey(uSubjectGuid))
                            {
                                uint uSubjectInst = fParent.SimInstanceFromID[uSubjectGuid];
                                sGUIDName = fParent.SimNameFromInstance[uSubjectInst];
                            }
                            // string sSubjectName = string.Format("SimID=0x{0:X8}", uSubjectGuid);
                        }
                    }
                    else
                    {
                        if (LotNameFromInstance.ContainsKey(uMemoryData[i]))
                            sLotName = "Lot(" + LotNameFromInstance[uMemoryData[i]] + ")  ";
                        if (FamilyNameFromInstance.ContainsKey(uMemoryData[i]))
                            sFamilyName = "Family(" + FamilyNameFromInstance[uMemoryData[i]] + ")  ";
                        if (fParent.SimNameFromInstance.ContainsKey(uMemoryData[i]))
                            sSimName = "Sim(" + fParent.SimNameFromInstance[uMemoryData[i]] + ")  ";
                    }
                    fLog.WriteLine(sIndentMemoryItem + string.Format("Data[{0}]: 0x{1:X4}   {2}{3}{4}{5}",
                        i, uMemoryData[i], sLotName, sFamilyName, sSimName, sGUIDName));
                }
                #endregion // LogMemoryData
            }

            try
            {
                if (!bExists)
                {
                    // If uInstance is invalid, then none of the associated memories can be valid (by definition)
                    sMemoryDesc = "  " + ((bRemoveUnknown) ? "Removed: " : "")
                        + sTS2TypeString[(int)instType] + " does not exist: " + sMemoryDesc + sMemoryName;
                    fParent.AddToList(sMemoryDesc);
                    bValidMemory = false;
                }
                else if (bMemoryAboutSelf || bMemoryAboutSim || bMemoryAboutObject || bMemoryAboutUnknown)
                {
                    Debug.Assert(TS2Type.Sim == instType);  // Memories should only belong to sims
                    bValidMemory = IsValidMemoryItem(uInstance, uMemoryGuid, bMemory, iInventoryNumber);
                }
                else if (bItemNotMemory)
                    bValidMemory = IsValidNonMemoryItem(uMemoryGuid, bMemory, iInventoryNumber);
                else if (bInventory || (0 != iInventoryNumber))
                {
                    // ToDo: check if valid object?
                    if (bMemory)
                    {
                        sMemoryDesc = "  Incorrectly labeled as Memory: " + sMemoryDesc;
                        fParent.AddToList(sMemoryDesc);
                        bValidMemory = false;
                    }
                    else
                        bValidMemory = true;
                    if (fParent.DisplayAllMemories && bValidMemory && !bRemoveUnknown)
                    {
                        sMemoryDesc = "  Valid: " + sMemoryDesc + 
                            ((0 == iInventoryNumber) ? "[Inventory] " : "") + sMemoryName;
                        fParent.AddToList(sMemoryDesc);
                    }
                }
                else
                    bValidMemory = UnknownMemoryItem();
            }
            catch
            {
                // Assume that there has been no AddToList for this item.
                fParent.AddToList("  Invalid data structure: " + sInstanceName + ": " + sMemoryName);
            }

            BW.Write(iDataCount);
            for (int i = 0; i < iDataCount; i++)
                BW.Write(uMemoryData[i]);

            // For now, do not remove invalid sim memories unless the sim doesn't exist.
            if (bExists && Test_KeepSimsMemory && (instType == TS2Type.Sim))
                return true;

            if (!bValidMemory)
                BW.BaseStream.Position = iIndex;    // Remove this memory item
            return bValidMemory;
        }

        private string DescribeInvalidMemory(bool bValidOwner, bool bValidSubject, bool bSelf)
        {
            if (bSelf && bValidOwner && bValidSubject)
                return "  " + sFixed + "Owner and Subject are different: ";
            if (!bValidOwner && bValidSubject)
                return "  " + sFixed + "Owner does not exist: ";
            if (bValidOwner && !bValidSubject)
                return "  " + sFixed + "Subject does not exist: ";
            return "  " + sFixed + "Neither Owner nor Subject exist: ";
        }

        // ToDo: What is a reasonable invalid value?
        private const uint uSubjectInstInvalid = 0xFFFFFFFF;
        private const uint uMysterySimGuid = 0x6DD33865;

        private bool IsValidMemoryItem(uint uInstance, uint uMemoryGuid, bool bMemory, int iInventoryNumber)
        {
            // ToDo: Invalid if length not 12 or 13; too few data items already caught one level up.
            Debug.Assert((12 == uMemoryData.Length)
                      || (13 == uMemoryData.Length));

            // ToDo: Invalid if not positive or negative?
            Debug.Assert((0x0000 == uMemoryData[0])     // Green (positive memory)
                      || (0x0004 == uMemoryData[0]));   // Red   (negative memory)

            Debug.Assert((0 == uMemoryData[1])          // Year  or 0
                      || (1997 == uMemoryData[1]));     //  Why 1997?
            Debug.Assert(uMemoryData[2] <= 12);         // Month or 0
            Debug.Assert(uMemoryData[3] <= 31);         // Day   or 0

            short iInitialValue = (short)uMemoryData[7];// Initial Value
            short iMinimumValue = (short)uMemoryData[8];// Minimum Value
            short iDailyDecay = (short)uMemoryData[9];  // Daily Decay
            short iCurrentValue = (short)uMemoryData[10];   // Current Value
            /*
            Debug.Assert(0 <= iMinimumValue);           // Odd that Minimum Value is almost always positive
            Debug.Assert(0 <= iDailyDecay);             // Odd that Daily Decay is almost always positive
            if ((0 != iInitialValue) && (-5 != iInitialValue))
            {
                Debug.Assert(Math.Abs(iMinimumValue) <= Math.Abs(iInitialValue));
                Debug.Assert(Math.Abs(iDailyDecay) <= Math.Abs(iInitialValue));
                Debug.Assert(Math.Abs(iCurrentValue) <= Math.Abs(iInitialValue));
                Debug.Assert(Math.Abs(iCurrentValue) >= Math.Abs(iMinimumValue));
            } */
            Debug.Assert((uMemoryData[11] <= 8)         // Unknown
                      || (0x0010 == uMemoryData[11])
                      || (0x0020 == uMemoryData[11]));

            bool bValidOwner = false;
            ushort uOwnerInst = uMemoryData[4];
            string sOwnerName = string.Format("0x{0:X4}", uOwnerInst);
            if (bValidOwner = fParent.SimNameFromInstance.ContainsKey(uOwnerInst))
                sOwnerName = fParent.SimNameFromInstance[uOwnerInst];

            bool bValidSubject = false;
            uint uSubjectInst = uSubjectInstInvalid;
            uint uSubjectGuid = (uint)((uMemoryData[6] << 16) | uMemoryData[5]);
            string sSubjectName = string.Format("SimID=0x{0:X8}", uSubjectGuid);
            if (uMysterySimGuid == uSubjectGuid)
            {
                sSubjectName = "Mystery Sim";
                bValidSubject = true;
            }
            else if (bValidSubject = fParent.SimInstanceFromID.ContainsKey(uSubjectGuid))
            {
                // If SimInstanceFromID contains the Instance, then so will fParent.SimNameFromInstance
                uSubjectInst = fParent.SimInstanceFromID[uSubjectGuid];
                sSubjectName = fParent.SimNameFromInstance[uSubjectInst];
            }

            if (uInstance != uOwnerInst)
            {
                if (bValidOwner)
                    sMemoryDesc += "Gossip about ";
                /* ToDo: Is user 0x0000 ever valid?
                else if ((0x0000 == uOwnerInst)
                      && ((0x0C8CB8A4 == uMemoryGuid) || (0x4DBF998E == uMemoryGuid)))      // Memory - Knowledge - Death
                {
                    // ToDo: Determine whether these memories are valid or invalid
                    //       Occurs in N002 & N003, but mostly in custom hoods
                    sMemoryDesc += "Gossip about neighborhood ";
                    bValidOwner = true;
                } */
                else // ToDo: How to tell the difference between gossip and a memory with an incorrect Owner?
                    sMemoryDesc += "Owner ";
                sMemoryDesc += sOwnerName + ": ";
            }
            sMemoryDesc += sMemoryName;

            if (bMemoryAboutSelf)
            {
                if (uOwnerInst != uSubjectInst)
                    sMemoryDesc += " (Subject: " + sSubjectName + ")";
            }
            else if (bMemoryAboutSim)
                sMemoryDesc = sMemoryDesc.Replace("$Subject", sSubjectName);
            else if (bMemoryAboutObject)
            {
                if (bValidSubject)
                {
                    sMemoryDesc = "  Unexpected Sim Subject: " + sMemoryDesc;
                    sMemoryDesc += " (Subject: " + sSubjectName + ")";
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
                // ToDo: Check whether subject is a valid object?
                //       Difficult to know all valid objects, but could at least translate those we know.
                bValidSubject = true;
            }
            else if (bMemoryAboutUnknown)
            {
                // We have no idea whether the subject is valid or not...
                bValidSubject = true;
            }
            else // If memory isn't known, we have no way to know whether it's valid
            {
                if (fParent.DisplayAllMemories)
                {
                    sMemoryDesc = "  Unknown: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                }
                return true;
            }

            if (bValidOwner && bValidSubject && (!bMemoryAboutSelf || (uOwnerInst == uSubjectInst)))
            {
                if (!bMemory)
                {
                    sMemoryDesc = "  Not labeled as Memory: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
                if (0 != iInventoryNumber)
                {
                    sMemoryDesc = "  Incorrectly labeled as Inventory: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }

                // At this point, we have a valid memory, except for the Data[12]=Subject Instance check.
                // This means that we can fix any problems with Data[12] as we find them.
                if ((bMemoryAboutSelf || bMemoryAboutSim) && (uSubjectGuid != uMysterySimGuid))
                {
                    if (12 < uMemoryData.Length)
                    {
                        if (uSubjectInst != uMemoryData[12])
                        {
                            string sPrefix = "  ";
                            sMemoryDesc = "Incorrect Subject Instance: " + sMemoryDesc + string.Format(" (Subject Instance=0x{0:X4})", uMemoryData[12]);
                            if (bRemoveUnknown)
                            {
                                // Fix subject instance
                                uMemoryData[12] = (ushort)uSubjectInst;
                                bMemoryRecordChanged = true;
                                sPrefix = "  Fixed: ";
                            }
                            sMemoryDesc = sPrefix + sMemoryDesc;
                            fParent.AddToList(sMemoryDesc);
                            return bRemoveUnknown;
                        }
                    }
                    else
                    {
                        string sPrefix = "  ";
                        sMemoryDesc = "Missing Subject Instance: " + sMemoryDesc;
                        if (bRemoveUnknown)
                        {
                            // Expand array, then add correct subject instance
                            ushort[] uNewData = new ushort[13];
                            Array.Copy(uMemoryData, uNewData, uMemoryData.Length);
                            iDataCount = 13;
                            uMemoryData = uNewData;

                            uMemoryData[12] = (ushort)uSubjectInst;
                            bMemoryRecordChanged = true;
                            sPrefix = "  Fixed: ";
                        }
                        sMemoryDesc = sPrefix + sMemoryDesc;
                        fParent.AddToList(sMemoryDesc);
                        return bRemoveUnknown;
                    }
                }
                else if (bMemoryAboutObject || ((bMemoryAboutSelf || bMemoryAboutSim) && (uSubjectGuid == uMysterySimGuid)))
                {
                    if (12 < uMemoryData.Length)
                    {
                        // For now, assume that it is OK if the Subjects Instance is the Owner Instance
                        if ((uOwnerInst != uMemoryData[12]) || (uSubjectGuid == uMysterySimGuid))
                        {
                            string sPrefix = "  ";
                            sMemoryDesc = "Unexpected Subject Instance: " + sMemoryDesc + string.Format(" (Subject Instance=0x{0:X4})", uMemoryData[12]);
                            if (bRemoveUnknown)
                            {
                                // Truncate data array
                                iDataCount = 12;
                                bMemoryRecordChanged = true;
                                sPrefix = "  Fixed: ";
                            }
                            sMemoryDesc = sPrefix + sMemoryDesc;
                            fParent.AddToList(sMemoryDesc);
                            return bRemoveUnknown;
                        }
                    }
                }

                if (fParent.DisplayAllMemories /* && !bRemoveUnknown */)
                {
                    sMemoryDesc = "  Valid: " + sMemoryDesc;
                    sMemoryDesc += " ";
                    sMemoryDesc +=
                        (0x0000 == uMemoryData[0]) ? "[Positive] " :
                        (0x0004 == uMemoryData[0]) ? "[Negative] " :
                        "[Quality:Unknown] ";
                    if (0 != uMemoryData[1])
                        sMemoryDesc +=
                            "[Date:" + uMemoryData[1] + "/" + uMemoryData[2] + "/" + uMemoryData[3] + "] ";
                    if ((0 != iInitialValue) || (0 != iCurrentValue))
                        sMemoryDesc +=
                            "[Value:" + iCurrentValue + " (" + iInitialValue + "->" + iMinimumValue + " by " + iDailyDecay + ")] ";
                    fParent.AddToList(sMemoryDesc);
                }
                return true;
            }

            sMemoryDesc = DescribeInvalidMemory(bValidOwner, bValidSubject, bMemoryAboutSelf) + sMemoryDesc;
            fParent.AddToList(sMemoryDesc);
            return false;
        }

        private bool IsValidNonMemoryItem(uint uMemoryGuid, bool bMemory, int iInventoryNumber)
        {
            bool bValid = true;
            bool bFixed = false;

            sMemoryDesc += sMemoryName;
            if (0xCF6FF511 == uMemoryGuid)          // Token - UrnStone
                bValid = IsValidUrnstone(out bFixed);
            else if ((0x0EAA2454 == uMemoryGuid)    // Token - College - Frat Member
                  || (0x8E77E373 == uMemoryGuid))   // Token - Door Key
                bValid = IsValidLot0(uMemoryGuid);
            else if ((0xB4DBFC33 == uMemoryGuid)    // Token - Apartment NPC Resident
                  || (0xAFA0DE64 == uMemoryGuid)    // Token - Don't Mess With Romantic Rival
                  || (0xCF505AB0 == uMemoryGuid)    // Token - Furious
                  || (0xEFED8EC1 == uMemoryGuid))   // Token - Furious Reverse
                bValid = IsValidSim0();
            else if ((0xED4CB1A4 == uMemoryGuid)    // Controller - Pregnancy
                  || (0x0C9BF0FB == uMemoryGuid))   // Token - Inheritance
                bValid = IsValidSim1();
            else if ((0x2CBFC3EF == uMemoryGuid)    // Token - NPC - Banished From Lot
                  || (0x4CB2BCF7 == uMemoryGuid))   // Token - NPC - Familiar to Lot
                bValid = IsValidSim1NPCType2();
            else if (0xED2D4357 == uMemoryGuid)     // Token - NPC - Preselected
                bValid = IsValidSim1Sim2();

            if (bValid && !bFixed)
            {
                if (bMemory)
                {
                    sMemoryDesc = "  Incorrectly labeled as Memory: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
                if (0 != iInventoryNumber)
                {
                    sMemoryDesc = "  Incorrectly labeled as Inventory: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
                if (fParent.DisplayAllMemories && !bRemoveUnknown)
                {
                    sMemoryDesc = "  Valid: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                }
            }
            return bValid || bFixed;
        }

        private bool bUserWantsRemoveInvalidUrnstones = false;  // Will only be true if bRemoveUnknown

        private bool IsValidUrnstone(out bool bFixed)
        {
            Debug.Assert(4 < uMemoryData.Length);
            bFixed = false;                                 // Will only be true if bRemoveUnknown

            bool bValidLot = false;
            uint uLotInst = uMemoryData[0];
            string sLotName = string.Format("LotID=0x{0:X4}", uLotInst);
            R_DESC Lot = null;
            if (LotNameFromInstance.ContainsKey(uLotInst))
            {
                sLotName = LotNameFromInstance[uLotInst];
                Lot = LotDescFromInstance[uLotInst];
                // Only allow urnstones to be sent to residential and community lots.
                if ((0 == Lot.LotType) || (1 == Lot.LotType))
                    bValidLot = true;
            }

            bool bValidSim = false;
            uint uSimInst = uMemoryData[1];
            string sSimName = string.Format("0x{0:X4}", uSimInst);
            if (fParent.SimNameFromInstance.ContainsKey(uSimInst))
            {
                sSimName = fParent.SimNameFromInstance[uSimInst];
                // ToDo: check that the sim is dead.
                bValidSim = true;
            }

            bool bValidHood = false;
            bool bLotInHood = false;
            uint uHoodID = uMemoryData[4];
#if DEBUG
            /* ToDO: Remove.  Just for code coverage.  Specific to E001.
            if (0x84 == uLotInst)
                uHoodID = 2;
            else if (0x85 == uLotInst)
                uHoodID = 3;
             */
#endif
            string sHoodName = string.Format("HoodID=0x{0:X4}", uHoodID);
            if (HoodNameFromID.ContainsKey(uHoodID))
            {
                sHoodName = HoodNameFromID[uHoodID];
                bValidHood = true;
                if ((null != Lot) && (uHoodID == Lot.HoodID))
                    bLotInHood = true;
            }

            sMemoryDesc = sMemoryDesc.Replace("$Subject", sSimName).Replace("$Lot", sLotName).Replace("$Hood", sHoodName);
            string sPrefix = sFixed;
            if (bRemoveUnknown && bValidSim)
            {
                // Try to fix this urnstone token
                if (bValidLot && !bValidHood)
                {
                    uMemoryData[4] = (ushort)Lot.HoodID;            // Get Neighborhood number from the valid Lot
                    bMemoryRecordChanged = bFixed = true;
                    sPrefix = "Fixed: ";
                }
                else if (!bValidLot || !bLotInHood)
                {
                    R_DESC NewLot = GetGraveyardLot(uHoodID);
                    if (null != NewLot)
                    {
                        uMemoryData[0] = (ushort)NewLot.Instance;   // Lot number
                        uMemoryData[4] = (ushort)NewLot.HoodID;     // Neighborhood number
                        bMemoryRecordChanged = bFixed = true;
                        sPrefix = "Fixed: ";
                    }
                    else if (!bUserWantsRemoveInvalidUrnstones)
                    {
                        // User wants us to treat this memory as valid for now
                        bFixed = true;
                        sPrefix = "";
                    }
                }
            }
            if (!bValidLot || !bValidSim || !bValidHood || !bLotInHood)
            {
                if (!bValidSim)
                    sMemoryDesc = "  " + sFixed + "Sim does not exist: " + sMemoryDesc;
                else if (!bValidLot)
                    sMemoryDesc = "  " + sPrefix + "Missing or invalid lot: " + sMemoryDesc;
                else if (!bValidHood)
                    sMemoryDesc = "  " + sPrefix + "Neighborhood does not exist: " + sMemoryDesc;
                else if (!bLotInHood)
                    sMemoryDesc = "  " + sPrefix + "Lot is not in neighborhood: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            return true;
        }

        private R_DESC GetGraveyardLot(uint uHoodID)
        {
            if (GraveyardLots.ContainsKey(uHoodID))
                return GraveyardLots[uHoodID];

            if (GraveyardLots.ContainsKey(uAllHoods))
                return GraveyardLots[uAllHoods];

            if (bUserWantsRemoveInvalidUrnstones)
            { 
                /* The user has asked us to remove invalid urnstones, so don't try to fix */
                return null;
            }

            // Ask the driver to resolve the graveyard. The original WinForms
            // SelectGraveyardDialog + retry MessageBox is now the driver's job
            // (IHoodCheckContext.ResolveGraveyard); it returns one final choice:
            //   - SelectedLot != null            -> use it (UseForAll spans subhoods)
            //   - SelectedLot == null, !RemoveAll -> keep invalid urnstones for this hood
            //   - RemoveAllInvalid                -> remove all invalid urnstones
            List<R_DESC> potential = PotentialGraveyardLots.ContainsKey(uHoodID)
                ? PotentialGraveyardLots[uHoodID]
                : new List<R_DESC>();

            GraveyardChoice choice = fParent.ResolveGraveyard(uHoodID, potential) ?? GraveyardChoice.Keep();

            if (choice.RemoveAllInvalid)
            {
                bUserWantsRemoveInvalidUrnstones = true;
                return null;
            }
            if (choice.SelectedLot == null)
            {
                // Keep invalid urnstones for this neighborhood (recoverable later).
                GraveyardLots.Add(uHoodID, null);
                return null;
            }
            GraveyardLots.Add(choice.UseForAll ? uAllHoods : uHoodID, choice.SelectedLot);
            return choice.SelectedLot;
        }

        private bool IsValidLot0(uint uMemoryGuid)
        {
            bool bValidLot = false;
            uint uLotInst = uMemoryData[0];
            string sLotName = string.Format("0x{0:X4}", uLotInst);
            R_DESC Lot = null;
            if (LotNameFromInstance.ContainsKey(uLotInst))
            {
                sLotName = LotNameFromInstance[uLotInst];
                Lot = LotDescFromInstance[uLotInst];
                bValidLot = true;
            }
            sMemoryDesc = sMemoryDesc.Replace("$Lot", sLotName);
            if (!bValidLot)
            {
                sMemoryDesc = "  " + sFixed + "Lot does not exist: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            else if (0x0EAA2454 == uMemoryGuid)     // Token - College - Frat Member
            {
                if (3 != Lot.LotType)               // Not University: Greek House
                {
                    sMemoryDesc = "  " + sFixed + "Lot is Not a Greek House: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
            }
            /* Myne doors can be used on residential lots and possibly others.
            else if (0x8E77E373 == uMemoryGuid)     // Token - Door Key
            {
                if (2 != Lot.LotType)               // Not University: Dorm
                {
                    sMemoryDesc = "  " + sFixed + "Lot is Not a Dorm: " + sMemoryDesc;
                    fParent.AddToList(sMemoryDesc);
                    return false;
                }
            } */
            return true;
        }

        private bool IsValidSim0()
        {
            Debug.Assert(0 < uMemoryData.Length);

            bool bValidSim = false;
            uint uSimInst = uMemoryData[0];
            string sSimName = string.Format("0x{0:X4}", uSimInst);
            if (fParent.SimNameFromInstance.ContainsKey(uSimInst))
            {
                sSimName = fParent.SimNameFromInstance[uSimInst];
                bValidSim = true;
            }
            sMemoryDesc = sMemoryDesc.Replace("$Subject", sSimName);
            if (!bValidSim)
            {
                sMemoryDesc = "  " + sFixed + "Sim does not exist: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            return true;
        }

        private bool IsValidSim1()
        {
            Debug.Assert(1 < uMemoryData.Length);

            bool bValidSim = false;
            uint uSimInst = uMemoryData[1];
            string sSimName = string.Format("0x{0:X4}", uSimInst);
            if (fParent.SimNameFromInstance.ContainsKey(uSimInst))
            {
                sSimName = fParent.SimNameFromInstance[uSimInst];
                bValidSim = true;
            }
            sMemoryDesc = sMemoryDesc.Replace("$Subject", sSimName);
            if (!bValidSim)
            {
                sMemoryDesc = "  " + sFixed + "Sim does not exist: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            return true;
        }

        private bool IsValidSim1NPCType2()
        {
            Debug.Assert(2 < uMemoryData.Length);

            bool bValidSim = false;
            uint uSimInst = uMemoryData[1];
            string sSimName = string.Format("0x{0:X4}", uSimInst);
            if (fParent.SimNameFromInstance.ContainsKey(uSimInst))
            {
                sSimName = fParent.SimNameFromInstance[uSimInst];
                // ToDo: Check that sim is the right NPC type. (Token - NPC - Familiar to Lot)
                bValidSim = true;
            }

            // bool bValidType = false;
            uint uNPCID = uMemoryData[2];
            string sNPCType = string.Format("NPC Type = 0x{0:X4}", uNPCID);
            if (NPCTypeFromID.ContainsKey(uNPCID))
            {
                sNPCType = NPCTypeFromID[uNPCID];
                // bValidType = true;
            }

            sMemoryDesc = sMemoryDesc.Replace("$NPCType", sNPCType).Replace("$Subject", sSimName);
            if (!bValidSim)
            {
                sMemoryDesc = "  " + sFixed + "Sim does not exist: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            return true;
        }

        private bool IsValidSim1Sim2()
        {
            Debug.Assert(2 < uMemoryData.Length);

            bool bValidSim1 = false;
            uint uSim1Inst = uMemoryData[1];
            string sSim1Name = string.Format("0x{0:X4}", uSim1Inst);
            if (fParent.SimNameFromInstance.ContainsKey(uSim1Inst))
            {
                sSim1Name = fParent.SimNameFromInstance[uSim1Inst];
                // ToDo: Check that sim is the right NPC type. (Token - NPC - Preselected)
                bValidSim1 = true;
            }

            bool bValidSim2 = false;
            uint uSim2Inst = uMemoryData[2];
            string sSim2Name = string.Format("0x{0:X4}", uSim2Inst);
            if (fParent.SimNameFromInstance.ContainsKey(uSim2Inst))
            {
                sSim2Name = fParent.SimNameFromInstance[uSim2Inst];
                bValidSim2 = true;
            }

            sMemoryDesc = sMemoryDesc.Replace("$Sim1", sSim1Name).Replace("$Sim2", sSim2Name);
            if (!bValidSim1 || !bValidSim2)
            {
                if (!bValidSim1 && bValidSim2)
                    sMemoryDesc = "  " + sFixed + "First sim does not exist: " + sMemoryDesc;
                else if (bValidSim1 && !bValidSim2)
                    sMemoryDesc = "  " + sFixed + "Second sim does not exist: " + sMemoryDesc;
                else
                    sMemoryDesc = "  " + sFixed + "Neither sim exists: " + sMemoryDesc;
                fParent.AddToList(sMemoryDesc);
                return false;
            }
            return true;
        }

        private bool UnknownMemoryItem()
        {
            if (fParent.DisplayAllMemories)
            {
                sMemoryDesc = "  Unknown: " + sMemoryDesc + sMemoryName;
                fParent.AddToList(sMemoryDesc);
            }
            return true;
        }

        private void InitializeNPCFamilyFromInstance()
        {
            // These known families may not have a FAMI entry in the neighborhood,
            // but they are recognized by the game.
            NPCFamilyFromInstance.Clear();
            NPCFamilyFromInstance.Add(0x7FF1, "Tropical Locals");
            NPCFamilyFromInstance.Add(0x7FF2, "Mountain Locals");
            NPCFamilyFromInstance.Add(0x7FF3, "Asian Locals");
            NPCFamilyFromInstance.Add(0x7FF4, "Tourists");
            NPCFamilyFromInstance.Add(0x7FF6, "Garden Club");
            NPCFamilyFromInstance.Add(0x7FF7, "Display Pets - In Use");
            NPCFamilyFromInstance.Add(0x7FF8, "Display Pets - Available");
            NPCFamilyFromInstance.Add(0x7FF9, "Orphan Pets");
            NPCFamilyFromInstance.Add(0x7FFA, "Strays");
            NPCFamilyFromInstance.Add(0x7FFB, "Bob the Builder");
            NPCFamilyFromInstance.Add(0x7FFC, "Downtownies");
            NPCFamilyFromInstance.Add(0x7FFD, "Orphans");
            NPCFamilyFromInstance.Add(0x7FFE, "Townies");
            NPCFamilyFromInstance.Add(0x7FFF, "Service NPCs");
        }

        private void InitializeNPCTypeFromID()
        {
            NPCTypeFromID.Clear();
            NPCTypeFromID.Add(0x01, "EP1 Bartender");
            NPCTypeFromID.Add(0x02, "Bartender");
            NPCTypeFromID.Add(0x03, "Boss");
            NPCTypeFromID.Add(0x04, "Burglar");
            NPCTypeFromID.Add(0x05, "School Bus Driver");
            NPCTypeFromID.Add(0x06, "EP1 Streaker");
            NPCTypeFromID.Add(0x07, "EP1 Coach");
            NPCTypeFromID.Add(0x08, "EP1 Cook");
            NPCTypeFromID.Add(0x09, "Police Officer");
            NPCTypeFromID.Add(0x0A, "Delivery Person");
            NPCTypeFromID.Add(0x0B, "Exterminator");
            NPCTypeFromID.Add(0x0C, "Fire Fighter");
            NPCTypeFromID.Add(0x0D, "Gardener");
            NPCTypeFromID.Add(0x0E, "Barista");
            NPCTypeFromID.Add(0x0F, "Grim Reaper");
            NPCTypeFromID.Add(0x10, "Repairman");
            NPCTypeFromID.Add(0x11, "Headmaster");
            NPCTypeFromID.Add(0x12, "Gypsy Matchmaker");
            NPCTypeFromID.Add(0x13, "Maid");
            NPCTypeFromID.Add(0x14, "Mail Carrier");
            NPCTypeFromID.Add(0x15, "Nanny");
            NPCTypeFromID.Add(0x16, "Newspaper Delivery Person");
            NPCTypeFromID.Add(0x17, "Pizza Delivery Person");
            NPCTypeFromID.Add(0x18, "EP1 Professor");
            NPCTypeFromID.Add(0x19, "EP1 Evil Mascot");
            NPCTypeFromID.Add(0x1A, "Repoman");
            NPCTypeFromID.Add(0x1B, "EP1 Cheerleader");
            NPCTypeFromID.Add(0x1C, "EP1 Mascot");
            NPCTypeFromID.Add(0x1D, "Social Bunny");
            NPCTypeFromID.Add(0x1E, "Social Worker");
            NPCTypeFromID.Add(0x1F, "Register Clerk");
            NPCTypeFromID.Add(0x20, "Therapist");
            NPCTypeFromID.Add(0x21, "EP2 Chineese Delivery Person");
            NPCTypeFromID.Add(0x22, "EP2 Dining Podium Host");
            NPCTypeFromID.Add(0x23, "EP2 Server");
            NPCTypeFromID.Add(0x24, "EP2 Chef");
            NPCTypeFromID.Add(0x25, "EP2 DJ");
            NPCTypeFromID.Add(0x26, "Ms. Crumplebottom");
            NPCTypeFromID.Add(0x27, "EP2 Grand Vampyre");
            NPCTypeFromID.Add(0x28, "EP3 Servo");
            NPCTypeFromID.Add(0x29, "EP3 Reporter");
            NPCTypeFromID.Add(0x2A, "EP3 Salon Stylist");
            NPCTypeFromID.Add(0x2B, "EP4 Wolf");
            NPCTypeFromID.Add(0x2C, "EP4 Wolf LOTP");
            NPCTypeFromID.Add(0x2D, "EP4 Skunk");
            NPCTypeFromID.Add(0x2E, "EP4 Animal Control Officer");
            NPCTypeFromID.Add(0x2F, "EP4 Obedience Trainer");
            NPCTypeFromID.Add(0x30, "EP6 Masseuse");
            NPCTypeFromID.Add(0x31, "EP6 Hotel Bellhop");
            NPCTypeFromID.Add(0x32, "EP6 Villain");
            NPCTypeFromID.Add(0x33, "EP6 Tour Guide");
            NPCTypeFromID.Add(0x34, "EP6 Hermit");
            NPCTypeFromID.Add(0x35, "EP6 Ninja");
            NPCTypeFromID.Add(0x36, "EP6 Bigfoot");
            NPCTypeFromID.Add(0x37, "EP6 Hotel Housekeeper");
            NPCTypeFromID.Add(0x38, "EP6 Food Stand Chef");
            NPCTypeFromID.Add(0x39, "EP6 Fire Dancer");
            NPCTypeFromID.Add(0x3A, "EP6 Witch Doctor");
            NPCTypeFromID.Add(0x3B, "EP6 Ghost Captain");
            NPCTypeFromID.Add(0x3C, "EP7 Food Judge");
            NPCTypeFromID.Add(0x3D, "EP7 Genie");
            NPCTypeFromID.Add(0x3E, "Fixed DJ");
            NPCTypeFromID.Add(0x3F, "Fixed Gypsy");
            NPCTypeFromID.Add(0x40, "EP8 Witch 1");
            NPCTypeFromID.Add(0x41, "EP8 Breakdancer");
            NPCTypeFromID.Add(0x42, "EP8 Spectral Cat");
            NPCTypeFromID.Add(0x44, "EP8 Landlord");
            NPCTypeFromID.Add(0x45, "EP8 Butler");
            NPCTypeFromID.Add(0x48, "EP8 Witch 2");
        }

        public static void InitializeMemoryNameFromID()
        {
            // List of Memories which require the valid Owner (Data 4)
            // as both the Subject GUID (Data 5-6) and Subject Instance (Data 12)
            MemoryAboutSelf.Clear();
            MemoryAboutSelf.Add(0x708FBCA5, "Achieved a Top-Ranked Business");                          // Memory - EP3 - Built A Top Ranked Business
            MemoryAboutSelf.Add(0xB1A31B27, "Became a Werewolf");                                       // Memory - EP4 - Became Werewolf
            MemoryAboutSelf.Add(0x9507F579, "Became A Witch / Warlock (bad)");                          // Memory - EP8 - Witches - Become a Witch (bad)
            MemoryAboutSelf.Add(0x3507F569, "Became A Witch / Warlock (good)");                         // Memory - EP8 - Witches - Become a Witch (good)
            MemoryAboutSelf.Add(0xAEB89C46, "Became a Zombie (Fear)");                                  // Memory - EP1 - Became Zombie - Fear
            MemoryAboutSelf.Add(0xAEB89C39, "Became a Zombie (Want)");                                  // Memory - EP1 - Became Zombie - Want
            MemoryAboutSelf.Add(0x4EB89947, "Became Big Man or Woman on Campus");                       // Memory - College - Big On Campus
            MemoryAboutSelf.Add(0x6EB8B6EC, "Became Business Tycoon");                                  // Memory - Career - Top - Business
            MemoryAboutSelf.Add(0x6EB8C2A3, "Became Captain Hero");                                     // Memory - Career - Top - Law Enforcement
            MemoryAboutSelf.Add(0x2EB8B5F7, "Became Celebrity Chef");                                   // Memory - Career - Top - Culinary
            MemoryAboutSelf.Add(0x342216C2, "Became City Planner");                                     // Memory - Career - Top - Architecture
            MemoryAboutSelf.Add(0x4EB8B6D6, "Became Criminal Mastermind");                              // Memory - Career - Top - Criminal
            MemoryAboutSelf.Add(0x525781B1, "Became Game Designer");                                    // Memory - Career - Top - Gamer
            MemoryAboutSelf.Add(0x6EB8B655, "Became General");                                          // Memory - Career - Top - Military
            MemoryAboutSelf.Add(0x6EB8B673, "Became Hall of Famer");                                    // Memory - Career - Top - Athlete
            MemoryAboutSelf.Add(0xB4221A51, "Became Hand of Poseidon");                                 // Memory - Career - Top - Oceanography
            MemoryAboutSelf.Add(0xD4221C09, "Became Head of SCIA");                                     // Memory - Career - Top - Intelligence
            MemoryAboutSelf.Add(0x6EB8B695, "Became Hospital Chief of Staff");                          // Memory - Career - Top - Medical
            MemoryAboutSelf.Add(0xEEB8B6AF, "Became Mad Scientist");                                    // Memory - Career - Top - Science
            MemoryAboutSelf.Add(0x0EB8B6C0, "Became Mayor");                                            // Memory - Career - Top - Politics
            MemoryAboutSelf.Add(0x525781A9, "Became Media Magnate");                                    // Memory - Career - Top - Journalism
            MemoryAboutSelf.Add(0xD2578CD5, "Became PlantSim");                                         // Memory - EP5 - Became Plantman
            MemoryAboutSelf.Add(0x14221C1B, "Became Prestidigitator");                                  // Memory - Career - Top - Entertainment
            MemoryAboutSelf.Add(0x2EB8B611, "Became Professional Party Guest");                         // Memory - Career - Top - Slacker
            MemoryAboutSelf.Add(0xD25781C0, "Became Rock God");                                         // Memory - Career - Top - Music
            MemoryAboutSelf.Add(0x725781B8, "Became Secretary of Education");                           // Memory - Career - Top - Education
            MemoryAboutSelf.Add(0xD257815D, "Became Space Pirate");                                     // Memory - Career - Top - Adventurer
            MemoryAboutSelf.Add(0x525781A0, "Became The Law");                                          // Memory - Career - Top - Law
            MemoryAboutSelf.Add(0x14221C29, "Became World Class Ballet Dancer");                        // Memory - Career - Top - Dance
            MemoryAboutSelf.Add(0x735EF9CC, "Bought a Vacation Home");                                  // Memory - EP6 - Bought a Vacation Home
            MemoryAboutSelf.Add(0x4FA36280, "Changed Aspiration");                                      // Memory - EP2 - Changed Aspiration
            MemoryAboutSelf.Add(0x145863EA, "Cheated Death");                                           // Memory - Knowledge - Cheated Death
            MemoryAboutSelf.Add(0xAEB899EB, "Completed Freshman Year");                                 // Memory - College - Year Complete - Freshman
            MemoryAboutSelf.Add(0x0EB89A0C, "Completed Junior Year");                                   // Memory - College - Year Complete - Junior
            MemoryAboutSelf.Add(0xEEB899FB, "Completed Sophomore Year");                                // Memory - College - Year Complete - Sophomore
            MemoryAboutSelf.Add(0x5507F5AC, "Cured Of Being A Witch / Warlock (bad)");                  // Memory - EP8 - Witches - Be Cured of Being a Witch (bad)
            MemoryAboutSelf.Add(0x7507F5A5, "Cured Of Being A Witch / Warlock (good)");                 // Memory - EP8 - Witches - Be Cured of Being a Witch (good)
            MemoryAboutSelf.Add(0x91A31B89, "Cured of Lycanthropy");                                    // Memory - EP4 - Cured Lycanthropy
            MemoryAboutSelf.Add(0x92578CE3, "Cured of Plantsimism");                                    // Memory - EP5 - Was Cured From Plantman
            MemoryAboutSelf.Add(0x54211028, "Discovered Aspirational Laboratories (Science)");          // Memory - EP7 - Secret Lot - Membership - Science
            MemoryAboutSelf.Add(0x94210FE5, "Discovered Desirable Discourse (Film and Literature)");    // Memory - EP7 - Secret Lot - Membership - Film and Literature
            MemoryAboutSelf.Add(0x5421105C, "Discovered Dreamy Fields (Sports)");                       // Memory - EP7 - Secret Lot - Membership - Sports
            MemoryAboutSelf.Add(0xD4211018, "Discovered Games of Glory (Games)");                       // Memory - EP7 - Secret Lot - Membership - Games
            MemoryAboutSelf.Add(0x942110A4, "Discovered My Muse - Music & Dance Studio");               // Memory - EP7 - Secret Lot - Membership - Music and Dance
            MemoryAboutSelf.Add(0xB4211033, "Discovered My Muse II - Art Studio (Arts and Crafts)");    // Memory - EP7 - Secret Lot - Membership - Arts and Crafts
            MemoryAboutSelf.Add(0x5421106A, "Discovered Peerless Park (Nature)");                       // Memory - EP7 - Secret Lot - Membership - Nature
            MemoryAboutSelf.Add(0x74211096, "Discovered Platinum Gym (Fitness)");                       // Memory - EP7 - Secret Lot - Membership - Fitness
            MemoryAboutSelf.Add(0x54210F5B, "Discovered Sue's (Secret) Kitchen (Cuisine)");             // Memory - EP7 - Secret Lot - Membership - Cuisine
            MemoryAboutSelf.Add(0x54211022, "Discovered Will's Garage (Tinkering)");                    // Memory - EP7 - Secret Lot - Membership - Tinkering
            MemoryAboutSelf.Add(0x2DCA2A43, "Earned $5,000");                                           // Memory - Wealth - Earn 5000
            MemoryAboutSelf.Add(0x6DCA2A62, "Earned $10,000");                                          // Memory - Wealth - Earn 10000
            MemoryAboutSelf.Add(0x6DCA2A7C, "Earned $25,000");                                          // Memory - Wealth - Earn 25000
            MemoryAboutSelf.Add(0x4DCA2A85, "Earned $50,000");                                          // Memory - Wealth - Earn 50000
            MemoryAboutSelf.Add(0x8DD217A1, "Earned $100,000");                                         // Memory - Wealth - Earn 100000
            MemoryAboutSelf.Add(0x2EB8B742, "Earned Lots of Money");                                    // Memory - Lifetime - Earn Lots of Money
            MemoryAboutSelf.Add(0x3229767A, "Fishing Master");                                          // Memory - EP5 - Badge - Fishing - Gold
            MemoryAboutSelf.Add(0x3507F664, "Got A Bad Reputation");                                    // Memory - EP8 - Reputation - Have Bad Reputation
            MemoryAboutSelf.Add(0xF507F65B, "Got A Good Reputation");                                   // Memory - EP8 - Reputation - Have Good Reputation
            MemoryAboutSelf.Add(0x8CAB102B, "Got Abducted by Aliens");                                  // Memory - Knowledge - Alien Abduction
            MemoryAboutSelf.Add(0x4CAB11D3, "Got an A+ Report Card");                                   // Memory - Knowledge - First A+ Report Card 
            MemoryAboutSelf.Add(0x0CAB114C, "Got into Private School");                                 // Memory - Knowledge - Private School (child) - Win
            MemoryAboutSelf.Add(0xEEB89A8A, "Got Put On Academic Probation");                           // Memory - College - Academic Probation
            MemoryAboutSelf.Add(0xCCAB1176, "Got Rejected from Private School");                        // Memory - Knowledge - Private School (child) - Lose
            MemoryAboutSelf.Add(0xEC8F8DCB, "Got Shocked");                                             // Memory - Knowledge - Electrocution
            MemoryAboutSelf.Add(0xF1A32288, "Got Sprayed By Skunk");                                    // Memory - EP4 - Sprayed By Skunk
            MemoryAboutSelf.Add(0x135EFA2A, "Got Voodoo Doll");                                         // Memory - EP6 - Got Voodoo Doll
            MemoryAboutSelf.Add(0x8EB89A2B, "Graduated");                                               // Memory - College - Graduated
            MemoryAboutSelf.Add(0xAEB89A56, "Graduated with Honors - Cum Laude");                       // Memory - College - Graduated w/Honors
            MemoryAboutSelf.Add(0x4EBF41FD, "Graduated with Honors - Magna Cum Laude");                 // Memory - College - Graduated High Honors
            MemoryAboutSelf.Add(0x2EBF4F61, "Graduated with Honors - Summa Cum Laude");                 // Memory - College - Graduated Ultra Mega Honors
            MemoryAboutSelf.Add(0xEDB54F39, "Grew Up Badly");                                           // Memory - Grow Up - Bad
            MemoryAboutSelf.Add(0x8DB54EC1, "Grew Up Well");                                            // Memory - Grow Up - Good
            MemoryAboutSelf.Add(0x0DE48B8F, "Had a Great Anniversary Party");                           // Memory - Reputation - Party - Anniversary - Success
            MemoryAboutSelf.Add(0xCDE48B2A, "Had a Great Birthday Party");                              // Memory - Reputation - Party - Birthday - Success
            MemoryAboutSelf.Add(0xEEB89C9E, "Had a Great Graduation Party");                            // Memory - College - Grad Party - Good
            MemoryAboutSelf.Add(0x0DB65FE1, "Had a Great Party");                                       // Memory - Reputation - Party - Success
            MemoryAboutSelf.Add(0x2DE48B5A, "Had a Great Wedding Party");                               // Memory - Reputation - Party - Wedding - Success
            MemoryAboutSelf.Add(0x4EB89CAB, "Had a Horrible Graduation Party");                         // Memory - College - Grad Party - Bad
            MemoryAboutSelf.Add(0xEDE48BE8, "Had a Lousy Anniversary Party");                           // Memory - Reputation - Party - Anniversary - Failure
            MemoryAboutSelf.Add(0x0DE48BB4, "Had a Lousy Birthday Party");                              // Memory - Reputation - Party - Birthday - Failure
            MemoryAboutSelf.Add(0xCDB65FF7, "Had a Lousy Party");                                       // Memory - Reputation - Party - Failure
            MemoryAboutSelf.Add(0x6DE48BCC, "Had a Lousy Wedding Party");                               // Memory - Reputation - Party - Wedding - Failure
            MemoryAboutSelf.Add(0xEC8F7C67, "Had an Accident");                                         // Memory - Reputation - Bladder Failure
            MemoryAboutSelf.Add(0x6DD21BBA, "Had an Accident at a Party");                              // Memory - Reputation - Bladder Failure - At Party
            MemoryAboutSelf.Add(0xF257825F, "I made a wish!");                                          // Memory - EP5 - Make Wish
            MemoryAboutSelf.Add(0x52579BE1, "Joined Garden Club");                                      // Memory - EP5 - Joined Gardening Club
            MemoryAboutSelf.Add(0x0EB89913, "Joined Greek House");                                      // Memory - College - Joined Greek House
            MemoryAboutSelf.Add(0x9507F604, "Learned About Fire Safety");                               // Memory - EP8 - Study Topics - Learn Fire Prevention
            MemoryAboutSelf.Add(0x3507F620, "Learned About Lifelong Happiness");                        // Memory - EP8 - Study Topics - Learn Lifelong Happiness
            MemoryAboutSelf.Add(0xB507F62A, "Learned About Physiology");                                // Memory - EP8 - Study Topics - Learn Physiology
            MemoryAboutSelf.Add(0x5507F613, "Learned Anger Management");                                // Memory - EP8 - Study Topics - Learn Anger Management
            MemoryAboutSelf.Add(0xD507F638, "Learned Couples Counseling");                              // Memory - EP8 - Study Topics - Learn Couples Counseling
            MemoryAboutSelf.Add(0x135EFA59, "Learned Hula Dance");                                      // Memory - EP6 - Learned Hula Dance
            MemoryAboutSelf.Add(0x335EFA43, "Learned Local Greeting");                                  // Memory - EP6 - Learned Local Greeting
            MemoryAboutSelf.Add(0x735EFA21, "Learned New Massage");                                     // Memory - EP6 - Learned New Massage
            MemoryAboutSelf.Add(0xF4150B1F, "Learned Parenting");                                       // Memory - Knowledge - Learn Parenting
            MemoryAboutSelf.Add(0x335EFA51, "Learned Slap Dance");                                      // Memory - EP6 - Learned Slap Dance
            MemoryAboutSelf.Add(0x735EFA76, "Learned to Teleport");                                     // Memory - EP6 - Learned to Teleport
            MemoryAboutSelf.Add(0xD41FDF7C, "Made 2 BFFs");                                             // Memory - EP7 - BFF - 2 BFFs
            MemoryAboutSelf.Add(0x941FDF8C, "Made 3 BFFs");                                             // Memory - EP7 - BFF - 3 BFFs
            MemoryAboutSelf.Add(0x141FDF9B, "Made 5 BFFs");                                             // Memory - EP7 - BFF - 5 BFFs
            MemoryAboutSelf.Add(0x141FDFA6, "Made 10 BFFs");                                            // Memory - EP7 - BFF - 10 BFFs
            MemoryAboutSelf.Add(0x2EB89999, "Made Dean's List");                                        // Memory - College - Deans List
            MemoryAboutSelf.Add(0x1269FBA0, "Made Wish (Backfire)");                                    // Memory - EP5 - Make Wish Backfire
            MemoryAboutSelf.Add(0x708FB3D4, "Master Cashier");                                          // Memory - EP3 - Badge - Cash Register - Gold
            MemoryAboutSelf.Add(0xF08FB30E, "Master Flower Arranger");                                  // Memory - EP3 - Badge - Flower Arranging - Gold
            MemoryAboutSelf.Add(0x922976C9, "Master Gardener");                                         // Memory - EP5 - Badge - Gardening - Gold
            MemoryAboutSelf.Add(0xF08FB371, "Master of Sales");                                         // Memory - EP3 - Badge - Sales - Gold
            MemoryAboutSelf.Add(0x908FB44C, "Master Restocker");                                        // Memory - EP3 - Badge - Stocking - Gold
            MemoryAboutSelf.Add(0x708FB34C, "Master Roboteer");                                         // Memory - EP3 - Badge - Robotery - Gold
            MemoryAboutSelf.Add(0xD08FB496, "Master Stylist");                                          // Memory - EP3 - Badge - Cosmetology - Gold
            MemoryAboutSelf.Add(0xD08FB1F4, "Master Toy Maker");                                        // Memory - EP3 - Badge - Toycrafting - Gold
            MemoryAboutSelf.Add(0x135EFA69, "Mastered Fire Dance");                                     // Memory - EP6 - Mastered Fire Dance
            MemoryAboutSelf.Add(0x6DB6632D, "Moved In");                                                // Memory - Reputation - Move In
            MemoryAboutSelf.Add(0xADB662DD, "Moved Out");                                               // Memory - Reputation - Move Out
            MemoryAboutSelf.Add(0x0EB898EA, "Never Went to College");                                   // Memory - College - Never Went
            MemoryAboutSelf.Add(0x108FBB1F, "Opened First Business");                                   // Memory - EP3 - Opened First Business
            MemoryAboutSelf.Add(0x8DB65FC4, "Passed Out");                                              // Memory - Reputation - Energy Failure
            MemoryAboutSelf.Add(0xD08FBDC9, "Plummeted in an Elevator");                                // Memory - EP3 - Fell In A Elevator
            MemoryAboutSelf.Add(0xCDCA2C50, "Ran Away");                                                // Memory - Family - Runaway (Teen)
            MemoryAboutSelf.Add(0x1420E5B6, "Reached Maximum Enthusiasm in Arts and Crafts");           // Memory - EP7 - Hobby Enthusiasm - Maximum - Arts and Crafts
            MemoryAboutSelf.Add(0xB420E579, "Reached Maximum Enthusiasm in Cuisine");                   // Memory - EP7 - Hobby Enthusiasm - Maximum - Cuisine
            MemoryAboutSelf.Add(0x9420E590, "Reached Maximum Enthusiasm in Film and Literature");       // Memory - EP7 - Hobby Enthusiasm - Maximum - Film and Literature
            MemoryAboutSelf.Add(0xB420E5D0, "Reached Maximum Enthusiasm in Fitness");                   // Memory - EP7 - Hobby Enthusiasm - Maximum - Fitness
            MemoryAboutSelf.Add(0x5420E59A, "Reached Maximum Enthusiasm in Games");                     // Memory - EP7 - Hobby Enthusiasm - Maximum - Games
            MemoryAboutSelf.Add(0x3420E5D9, "Reached Maximum Enthusiasm in Music and Dance");           // Memory - EP7 - Hobby Enthusiasm - Maximum - Music and Dance
            MemoryAboutSelf.Add(0x7420E5C7, "Reached Maximum Enthusiasm in Nature");                    // Memory - EP7 - Hobby Enthusiasm - Maximum - Nature
            MemoryAboutSelf.Add(0xB420E5AD, "Reached Maximum Enthusiasm in Science");                   // Memory - EP7 - Hobby Enthusiasm - Maximum - Science
            MemoryAboutSelf.Add(0x1420E5BE, "Reached Maximum Enthusiasm in Sports");                    // Memory - EP7 - Hobby Enthusiasm - Maximum - Sports
            MemoryAboutSelf.Add(0xF420E5A3, "Reached Maximum Enthusiasm in Tinkering");                 // Memory - EP7 - Hobby Enthusiasm - Maximum - Tinkering
            MemoryAboutSelf.Add(0x1507F5EE, "Reached Maximum Magic Skill");                             // Memory - EP8 - Witches - Reach Maximum Skill
            MemoryAboutSelf.Add(0xB507F5DB, "Reached Maximum Virtuousness");                            // Memory - EP8 - Witches - Reach Maximum Goodness
            MemoryAboutSelf.Add(0xF507F5E4, "Reached Maximum Wickedness");                              // Memory - EP8 - Witches - Reach Maximum Badness
            MemoryAboutSelf.Add(0x142261F9, "Reached Platinum Lifetime Aspiration");                    // Memory - EP7 - Misc - Reached Max LTA
            MemoryAboutSelf.Add(0x12579BF8, "Received a Perfect Garden Score");                         // Memory - EP5 - Get Perfect Garden Score
            MemoryAboutSelf.Add(0x0C8CC811, "Repoman!");                                                // Memory - Wealth - Repoman
            MemoryAboutSelf.Add(0xB257749F, "Saw First Snow");                                          // Memory - Weather - See First Snow
            MemoryAboutSelf.Add(0x4DCA2BC7, "Sold a Great Novel");                                      // Memory - Wealth - Novel - Sell High
            MemoryAboutSelf.Add(0x135EF9E5, "Swam in Ocean");                                           // Memory - EP6 - Swam in Ocean
            MemoryAboutSelf.Add(0x0CCF6565, "Was an Overachiever");                                     // Memory - Grow Up - Overachiever
            MemoryAboutSelf.Add(0x2F9F896B, "Was Cured of Vampirisim");                                 // Memory - EP2 - Was Cured From Vampirism
            MemoryAboutSelf.Add(0x335EF9BF, "Went on Far East Vacation");                               // Memory - EP6 - Went on Far East Vacation
            MemoryAboutSelf.Add(0xF35EF9AE, "Went on Island Vacation");                                 // Memory - EP6 - Went on Island Vacation
            MemoryAboutSelf.Add(0xB35EF9B9, "Went on Mountain Vacation");                               // Memory - EP6 - Went on Mountain Vacation
            MemoryAboutSelf.Add(0x335EFA3B, "Went on Tour");                                            // Memory - EP6 - Went on Tour
            MemoryAboutSelf.Add(0xEEB89880, "Went to College");                                         // Memory - College - Went to College
            MemoryAboutSelf.Add(0xF08FBC5B, "Won Best-of-the-Best Award");                              // Memory - EP3 - Won Best Of The Best Award
            MemoryAboutSelf.Add(0xF4223807, "Won First Cooking Contest");                               // Memory - EP7 - Hobby - Cuisine - Won First Cooking Contest
            MemoryAboutSelf.Add(0xAED2D3B5, "Won the Bain-Gordon Communications Fellowship");           // Memory - Scholarship04
            MemoryAboutSelf.Add(0x0ED2D3E9, "Won the Bui Engineering Award");                           // Memory - Scholarship07
            MemoryAboutSelf.Add(0x2ED2D351, "Won the Extraterrestrial Reparations Grant");              // Memory - Scholarship01
            MemoryAboutSelf.Add(0x0ED2D39D, "Won the Hogan Award for Athletics");                       // Memory - Scholarship03
            MemoryAboutSelf.Add(0x0ED2D3C7, "Won the Kim Prize for Hygienics");                         // Memory - Scholarship05
            MemoryAboutSelf.Add(0xAED2D431, "Won the London Culinary Arts Scholarship");                // Memory - Scholarship10
            MemoryAboutSelf.Add(0x8ED2EB23, "Won the Orphaned Sims Assistance Fund");                   // Memory - Scholarship14
            MemoryAboutSelf.Add(0x6ED2D48F, "Won the Phelps-Wilsonoff Billiards Prize");                // Memory - Scholarship12
            MemoryAboutSelf.Add(0x0ED2D472, "Won the Quigley Visual Arts Stipend");                     // Memory - Scholarship11
            MemoryAboutSelf.Add(0x2ED2D38B, "Won the SimCity Scholar's Grant");                         // Memory - Scholarship02
            MemoryAboutSelf.Add(0xAED2EB0B, "Won the Tsang Footwork Award");                            // Memory - Scholarship13
            MemoryAboutSelf.Add(0xEED2D410, "Won the Undead Educational Scholarship");                  // Memory - Scholarship09
            MemoryAboutSelf.Add(0xAED2D3DB, "Won the Will Wright Genius Grant");                        // Memory - Scholarship06
            MemoryAboutSelf.Add(0xCED2D3FA, "Won the Young Entrepreneurs Award");                       // Memory - Scholarship08
            MemoryAboutSelf.Add(0xB422415D, "Wrote First Novel");                                       // Memory - EP7 - Hobby - Film and Literature - Wrote First Novel

            // ToDo: Split into Sim vs. Pet
            // Memories which require a valid sim as both the Subject GUID (Data 5-6) and Subject Instance (Data 12)
            MemoryAboutSim.Clear();
            MemoryAboutSim.Add(0x1507F58E, "$Subject Became A Witch / Warlock (bad)");                  // Memory - EP8 - Witches - Sim is a Witch (bad)
            MemoryAboutSim.Add(0xD507F586, "$Subject Became A Witch / Warlock (good)");                 // Memory - EP8 - Witches - Sim is a Witch (good)
            MemoryAboutSim.Add(0x8EB89B88, "$Subject Became a Zombie (Want)");                          // Memory - EP1 - Sim Is Zombie - Want
            MemoryAboutSim.Add(0x31A33C37, "$Subject Became Mine");                                     // Memory - EP4 - Pet - New Mine
            MemoryAboutSim.Add(0x6DCA78C3, "$Subject Broke Up");                                        // Memory - Family - Break Up - Relative
            MemoryAboutSim.Add(0xEDCA2C7A, "$Subject Came Back");                                       // Memory - Family - Runaway - Return (Parent)
            MemoryAboutSim.Add(0x0C8CB8A4, "$Subject Died (General Memory)");                           // Memory - Knowledge - Death - General
            MemoryAboutSim.Add(0x4DBF998E, "$Subject Died (Strong Memory)");                            // Memory - Knowledge - Death - Strong
            MemoryAboutSim.Add(0xCEB89AEB, "$Subject Dropped Out");                                     // Memory - College - Sim Dropped Out
            MemoryAboutSim.Add(0x6DB654AC, "$Subject Got a D!");                                        // Memory - Knowledge - D Report Card - Family
            MemoryAboutSim.Add(0xEDD35A61, "$Subject Got Abducted by Aliens");                          // Memory - Knowledge - Sim Abducted
            MemoryAboutSim.Add(0x8DB6545D, "$Subject Got an A+");                                       // Memory - Knowledge - First A+ - Family
            MemoryAboutSim.Add(0x6DB54BF5, "$Subject Got Engaged");                                     // Memory - Family - Family Engagement
            MemoryAboutSim.Add(0x0EB89ACF, "$Subject Got Expelled");                                    // Memory - College - Got Expelled - Sim
            MemoryAboutSim.Add(0xECAB11A1, "$Subject Got into Private School");                         // Memory - Knowledge - Private School (parent) - Win
            MemoryAboutSim.Add(0x0DCA34BF, "$Subject Got Joined Union");                                // Memory - Family - Joined - Relative
            MemoryAboutSim.Add(0xADB54DDE, "$Subject Got Left at the Alter");                           // Memory - Family - Family Left at Altar
            MemoryAboutSim.Add(0xCDB54E20, "$Subject Got Married");                                     // Memory - Family - Family Married
            MemoryAboutSim.Add(0x0EB89A3A, "$Subject Graduated");                                       // Memory - College - Graduated - Sim
            MemoryAboutSim.Add(0xEEB89A6E, "$Subject Graduated with Honors - Cum Laude");               // Memory - College - Graduated w/Honors - Sim
            MemoryAboutSim.Add(0x4EBF4F53, "$Subject Graduated with Honors - Magna Cum Laude");         // Memory - College - Graduated High Honors - Sim
            MemoryAboutSim.Add(0xEEBF4F6B, "$Subject Graduated with Honors - Summa Cum Laude");         // Memory - College - Graduated Ultra Mega Honors - Sim
            MemoryAboutSim.Add(0x6DB54F80, "$Subject Grew Up Badly");                                   // Memory - Grow Up - Bad - Other Sim
            MemoryAboutSim.Add(0x4DB54F65, "$Subject Grew Up Well");                                    // Memory - Grow Up - Good - Other Sim
            MemoryAboutSim.Add(0xB1A3190C, "$Subject (Pet) Had Babies");                                // Memory - EP4 - Pet Had Babies (Sim)
            MemoryAboutSim.Add(0xAEB89B96, "$Subject is a Zombie (Fear)");                              // Memory - EP1 - Sim Is Zombie - Fear
            MemoryAboutSim.Add(0x91A33DDD, "$Subject Joined Pack");                                     // Memory - EP4 - Pet - New Pack Member
            MemoryAboutSim.Add(0xEDB54E9E, "$Subject Joined the Family");                               // Memory - Family - New Family Member
            MemoryAboutSim.Add(0x2EB899C3, "$Subject Made Dean's List");                                // Memory - College - Deans List - Sim
            MemoryAboutSim.Add(0x4DD35A70, "$Subject Met Aliens");                                      // Memory - Knowledge - Sim Met Alien
            MemoryAboutSim.Add(0x8EB89AA1, "$Subject on Academic Probation");                           // Memory - College - Academic Probation - Sim
            MemoryAboutSim.Add(0xF1A31932, "$Subject (Pet) Ran Away");                                  // Memory - EP4 - Pet Ran Away (Sim)
            MemoryAboutSim.Add(0x0CAB0CDF, "$Subject Ran Away");                                        // Memory - Family - Runaway (Parent)
            MemoryAboutSim.Add(0x0DB654D2, "$Subject Was an Overachiever");                             // Memory - Knowledge - Over Achiever - Family
            MemoryAboutSim.Add(0x31A31A18, "$Subject (Pet) Was Born");                                  // Memory - EP4 - Pet Was Born (Sim)
            MemoryAboutSim.Add(0x5507F5CD, "$Subject Was Cured Of Being A Witch / Warlock (bad)");      // Memory - EP8 - Witches - Sim is Cured of Being a Witch (bad)
            MemoryAboutSim.Add(0x9507F5C6, "$Subject Was Cured Of Being A Witch / Warlock (good)");     // Memory - EP8 - Witches - Sim is Cured of Being a Witch (good)
            MemoryAboutSim.Add(0x2CAB11B3, "$Subject was Rejected from Private School");                // Memory - Knowledge - Private School (parent) - Lose
            MemoryAboutSim.Add(0xB1A31788, "$Subject (Pet) Was Removed");                               // Memory - EP4 - Pet Was Removed
            MemoryAboutSim.Add(0x31A31960, "$Subject (Pet) Was Returned");                              // Memory - EP4 - Pet Was Returned (Sim)
            MemoryAboutSim.Add(0xCEB898BB, "$Subject Went to College");                                 // Memory - College - Sim Goes to College
            MemoryAboutSim.Add(0x6F9F6E45, "1st Date with $Subject");                                   // Memory - EP2 - 1st Date with Sim
            MemoryAboutSim.Add(0x8CAB0C70, "Adopted $Subject");                                         // Memory - Family - Adopt Child
            MemoryAboutSim.Add(0xF1A31D8B, "Became Best Friends with $Subject (Pet)");                  // Memory - EP4 - Became Best Friends With Pet
            MemoryAboutSim.Add(0x4F9F892B, "Became Vampire by $Subject");                               // Memory - EP2 - Became Vampire
            MemoryAboutSim.Add(0x4FA1FEA0, "Bit $Subject");                                             // Memory - EP2 - Bit Sim
            MemoryAboutSim.Add(0xADC8AC35, "Broke Up with Fiance $Subject (Fear)");                     // Memory - Family - Break Up Engagement (Fear)
            MemoryAboutSim.Add(0xADCFF200, "Broke Up with Fiance $Subject (Want)");                     // Memory - Family - Break Up Engagement (Want)
            MemoryAboutSim.Add(0x6C9BFA3A, "Broke Up with Spouse $Subject (Fear)");                     // Memory - Family - Break Up Spouse (Fear)
            MemoryAboutSim.Add(0x4DCA29ED, "Broke Up with Spouse $Subject (Want)");                     // Memory - Family - Break Up Spouse (Want)
            MemoryAboutSim.Add(0x4CD8D38C, "Broke Up with Steady $Subject");                            // Memory - Love - Break up with Steady
            MemoryAboutSim.Add(0x6DCC9ABD, "Broke Up with Steady $Subject (Want)");                     // Memory - Love - Break up with Steady (Want)
            MemoryAboutSim.Add(0xAC8CC7D9, "Burglar! $Subject");                                        // Memory - Wealth - Burglar
            MemoryAboutSim.Add(0x0CCA82D1, "Caught $Subject Cheating");                                 // Memory - Love - Affair - Catch Lover
            MemoryAboutSim.Add(0x91A333BA, "Chased by $Subject (Pet)");                                 // Memory - EP4 - Chased By Pet
            MemoryAboutSim.Add(0xD1A333E0, "Chased by Wolf $Subject");                                  // Memory - EP4 - Chased By Wolf
            MemoryAboutSim.Add(0x6CAB0CC4, "Child $Subject Taken by Social Worker");                    // Memory - Family - Social Worker
            MemoryAboutSim.Add(0x31A335C7, "Death of $Subject");                                        // Memory - EP4 - Pet Died
            MemoryAboutSim.Add(0x8CAB091A, "Did Public WooHoo with $Subject");                          // Memory - Love - WooHoo - Public
            MemoryAboutSim.Add(0x2C8CB358, "Did WooHoo with $Subject");                                 // Memory - Love - WooHoo
            MemoryAboutSim.Add(0xADCA2D1B, "Did WooHoo with NPC $Subject");                             // Memory - Love - WooHoo - NPC
            MemoryAboutSim.Add(0x6EB8B5AF, "Drank $Subject");                                           // Memory - EP1 - Drank Neighbor
            MemoryAboutSim.Add(0x6C8CB22A, "Fell in Love with $Subject");                               // Memory - Love - In Love
            MemoryAboutSim.Add(0xB507F528, "Found a Roommate $Subject");                                // Memory - EP8 - Apartment - Find a Roommate
            MemoryAboutSim.Add(0xEDB65489, "$Subject Got a D Report Card");                             // Memory - Knowledge - D Report Card
            MemoryAboutSim.Add(0x2DB64F87, "Got Busted (with $Subject)");                               // Memory - Family - Sneak Out - Busted
            MemoryAboutSim.Add(0x8CAB0993, "Got Caught Cheating by $Subject");                          // Memory - Love - Affair - Get Caught
            MemoryAboutSim.Add(0xEDB64D39, "Got Caught Sneaking Out by $Subject");                      // Memory - Family - Sneak Out - First Caught
            MemoryAboutSim.Add(0x8DD790A1, "Got Engaged To $Subject (Fear)");                           // Memory - Family - Engagement - Fear
            MemoryAboutSim.Add(0xAC9BF983, "Got Engaged To $Subject (Success)");                        // Memory - Family - Engagement - Success
            MemoryAboutSim.Add(0x2DD7CE1B, "Got Joined with $Subject, a Rich Sim");                     // Memory - Wealth - Joined Rich
            MemoryAboutSim.Add(0x4C9BFA0B, "Got Left at the Alter by $Subject");                        // Memory - Family - Marriage - Left at Altar
            MemoryAboutSim.Add(0x2DD790B1, "Got Married to $Subject (Fear)");                           // Memory - Family - Marriage - Fear
            MemoryAboutSim.Add(0x6C9BF9C1, "Got Married to $Subject (Success)");                        // Memory - Family - Marriage - Success
            MemoryAboutSim.Add(0xECAB0EEA, "Got Married to $Subject, a Rich Sim");                      // Memory - Wealth - Marry Rich
            MemoryAboutSim.Add(0x91A33C89, "Got Master $Subject");                                      // Memory - EP4 - Pet - Got Master
            MemoryAboutSim.Add(0x4C9BF9A7, "Got Rejected for Engagement to $Subject");                  // Memory - Family - Engagement - Failure
            MemoryAboutSim.Add(0xADB5508F, "Got Rejected for Going Steady by $Subject");                // Memory - Love - Going Steady - Failure
            MemoryAboutSim.Add(0x6DB8BEA5, "Got Rejected for Make Out by $Subject");                    // Memory - Love - Make Out Fail
            MemoryAboutSim.Add(0x4CAB0932, "Got Rejected for Public WooHoo with $Subject");             // Memory - Love - WooHoo - Public - Rejected
            MemoryAboutSim.Add(0x4C89E3FA, "Got Rejected for Very First Kiss by $Subject");             // Memory - Love - Very First Kiss - Failure
            MemoryAboutSim.Add(0xCC8CB6D8, "Got Rejected for WooHoo with $Subject");                    // Memory - Love - WooHoo - Rejected
            MemoryAboutSim.Add(0x8EB8B75D, "Graduated X Children from College ($Subject)");             // Memory - Lifetime - Graduate X Children
            MemoryAboutSim.Add(0x2C8CC73F, "Had $Subject");                                             // Memory - Family - Birth
            MemoryAboutSim.Add(0xD1A33A83, "Had $Subject! (Pet)");                                      // Memory - EP4 - Pet - Gave Birth (Pet)
            MemoryAboutSim.Add(0x6DB54A60, "Had 2 Best Friends ($Subject)");                            // Memory - Reputation - 2 Best Friends
            MemoryAboutSim.Add(0x8DB54A8B, "Had 3 Best Friends ($Subject)");                            // Memory - Reputation - 3 Best Friends
            MemoryAboutSim.Add(0x8DB54AA1, "Had 5 Best Friends ($Subject)");                            // Memory - Reputation - 5 Best Friends
            MemoryAboutSim.Add(0x2DB54ABB, "Had 10 Best Friends ($Subject)");                           // Memory - Reputation - 10 Best Friends
            MemoryAboutSim.Add(0xCDD2159E, "Had 30 Best Friends ($Subject)");                           // Memory - Reputation - 50 Best Friends
            MemoryAboutSim.Add(0xADB54B71, "Had 2 Loves at Once ($Subject)");                           // Memory - Love - 2 Simultaneous Loves
            MemoryAboutSim.Add(0x6DB54B9F, "Had 3 Loves at Once ($Subject)");                           // Memory - Love - 3 Simultaneous Loves
            MemoryAboutSim.Add(0x2DB54BB6, "Had 5 Loves at Once ($Subject)");                           // Memory - Love - 5 Simultaneous Loves
            MemoryAboutSim.Add(0x2DB54BCC, "Had 10 Loves at Once ($Subject)");                          // Memory - Love - 10 Simultaneous Loves
            MemoryAboutSim.Add(0x2DD215D0, "Had 30 Loves at Once ($Subject)");                          // Memory - Love - 50 Simultaneous Loves
            MemoryAboutSim.Add(0xF1A31DFE, "Had 2 Pet Best Friends ($Subject)");                        // Memory - EP4 - 2 Pet Best Friends
            MemoryAboutSim.Add(0xB1A31E24, "Had 3 Pet Best Friends ($Subject)");                        // Memory - EP4 - 3 Pet Best Friends
            MemoryAboutSim.Add(0x51A31E44, "Had 5 Pet Best Friends ($Subject)");                        // Memory - EP4 - 5 Pet Best Friends
            MemoryAboutSim.Add(0x71A31E61, "Had 10 Pet Best Friends ($Subject)");                       // Memory - EP4 - 10 Pet Best Friends
            MemoryAboutSim.Add(0x2F9F6F28, "Had a Bad Date with $Subject");                             // Memory - EP2 - Had Bad Date
            MemoryAboutSim.Add(0xCF9F6E94, "Had a Dream Date with $Subject");                           // Memory - EP2 - Had Dream Date
            MemoryAboutSim.Add(0x2DD3B15F, "Had a Family Reunion ($Subject)");                          // Memory - Family - Reunion
            MemoryAboutSim.Add(0xCF9F6EA7, "Had a Great Date with $Subject");                           // Memory - EP2 - Had Great Date
            MemoryAboutSim.Add(0x6F9F6F3C, "Had a Horrible Date with $Subject");                        // Memory - EP2 - Had Horrible Date
            MemoryAboutSim.Add(0x0CAB0956, "Had an Affair with $Subject");                              // Memory - Love - Affair
            MemoryAboutSim.Add(0x908FBE48, "Had First WooHoo with $Subject, the Robot");                // Memory - EP3 - First Woohoo With Robot
            MemoryAboutSim.Add(0xCC89C448, "Had Very First Kiss with $Subject");                        // Memory - Love - Very First Kiss
            MemoryAboutSim.Add(0xEEB89C6E, "Had Very First WooHoo with $Subject");                      // Memory - EP1 - Very First WooHoo
            MemoryAboutSim.Add(0xCEB8B7D3, "Had X Best Friends ($Subject)");                            // Memory - Lifetime - Have X Simultaneous Best Friends
            MemoryAboutSim.Add(0xEFDA8C42, "Had X Dream Dates ($Subject)");                             // Memory - Lifetime - EP2 - Have X Dream Dates
            MemoryAboutSim.Add(0xAFDA8C56, "Had X First Dates ($Subject)");                             // Memory - Lifetime - EP2 - Have X First Dates
            MemoryAboutSim.Add(0xEEB8B7B8, "Had X Grandchildren ($Subject)");                           // Memory - Lifetime - Have X Grandchildren
            MemoryAboutSim.Add(0x6EB8B7EC, "Had X Simultaneous Lovers ($Subject)");                     // Memory - Lifetime - Have X Simultaneous Lovers
            MemoryAboutSim.Add(0xD1B02096, "Had X Simultaneous Pet Best Friends ($Subject)");           // Memory - EP4 - Lifetime - Have X Simultaneous Pet Best Friends
            MemoryAboutSim.Add(0xECAB0ED1, "Inherited Money from $Subject");                            // Memory - Wealth - Inheritance
            MemoryAboutSim.Add(0xCD476A33, "Joined Union with $Subject");                               // Memory - Family - Joined - Success
            MemoryAboutSim.Add(0x2DD790BC, "Joined Union with $Subject (Fear)");                        // Memory - Family - Joined - Fear
            MemoryAboutSim.Add(0xF507F539, "Kicked Out Roommate $Subject");                             // Memory - EP8 - Apartment - Kick Out Roommate
            MemoryAboutSim.Add(0x2DB54AE3, "Kissed $Subject for the First Time");                       // Memory - Love - Kissed Sim
            MemoryAboutSim.Add(0xED9AB084, "Learned to Study from $Subject");                           // Memory - Knowledge - Be Taught to Study
            MemoryAboutSim.Add(0x4CAB0350, "Learned to Talk from $Subject");                            // Memory - Knowledge - Be Taught to Talk
            MemoryAboutSim.Add(0xECAB0FFD, "Learned to Walk from $Subject");                            // Memory - Grow Up - Be Taught to Walk
            MemoryAboutSim.Add(0xCC8CC7AC, "Lost $Subject as Best Friend");                             // Memory - Reputation - Lost Best Friend
            MemoryAboutSim.Add(0x6CAB108D, "Lost $Subject to the Grim Reaper");                         // Memory - Power - Grim Reaper - Lose
            MemoryAboutSim.Add(0x0CAB07DB, "Lost a Fight with $Subject");                               // Memory - Power - Fight - Lose
            MemoryAboutSim.Add(0x941FDF70, "Made a BFF with $Subject");                                 // Memory - EP7 - BFF - With
            MemoryAboutSim.Add(0x0EB89B63, "Made $Subject a Zombie (Fear)");                            // Memory - EP1 - Made Zombie - Fear
            MemoryAboutSim.Add(0xCEB89B53, "Made $Subject a Zombie (Want)");                            // Memory - EP1 - Made Zombie - Want
            MemoryAboutSim.Add(0x0C8CC785, "Made Best Friends with $Subject");                          // Memory - Reputation - First Best Friend
            MemoryAboutSim.Add(0xCDD790F9, "Made Enemies with $Subject");                               // Memory - Reputation - Enemy
            MemoryAboutSim.Add(0x4DB54B44, "Made Out With $Subject");                                   // Memory - Love - Make Out with Sim
            MemoryAboutSim.Add(0xEEB8B88D, "Marry Off X Children ($Subject)");                          // Memory - Lifetime - Marry off X Children
            MemoryAboutSim.Add(0xADD79121, "Met $Subject");                                             // Memory - Reputation - Met Someone
            MemoryAboutSim.Add(0x31A334D8, "Met $Subject (Pet)");                                       // Memory - EP4 - Met Pet
            MemoryAboutSim.Add(0x6CD0F6C6, "Met New Grandchild $Subject");                              // Memory - Family - Grandchild
            MemoryAboutSim.Add(0x11A318D4, "Obtained $Subject (Pet)");                                  // Memory - EP4 - Obtained a Pet
            MemoryAboutSim.Add(0x0CAB1286, "Potty Trained $Subject");                                   // Memory - Knowledge - Potty Trainer
            MemoryAboutSim.Add(0xACAB1271, "Potty Trained by $Subject");                                // Memory - Grow Up - Potty Trained
            MemoryAboutSim.Add(0xB269EBFA, "Produced Spore $Subject");                                  // Memory - Plantman - Produce Spore
            MemoryAboutSim.Add(0x4EB89B3B, "Resurrected $Subject");                                     // Memory - EP1 - Resurrected Sim
            MemoryAboutSim.Add(0x7507F647, "Reunited With $Subject");                                   // Memory - EP8 - Study Topics - Reunited with
            MemoryAboutSim.Add(0x7507F546, "Roommate $Subject Moved Out");                              // Memory - EP8 - Apartment - Roommate Leaves
            MemoryAboutSim.Add(0x8CAB106A, "Saved $Subject from the Grim Reaper");                      // Memory - Power - Grim Reaper - Win
            MemoryAboutSim.Add(0x4DA25D22, "Saw the Ghost of $Subject");                                // Memory - Knowledge - Saw Ghost of Sim
            MemoryAboutSim.Add(0x6DD790CD, "Saw the Ghost of $Subject (Fear)");                         // Memory - Knowledge - Saw Ghost of Sim - Fear
            MemoryAboutSim.Add(0x8DB64CFA, "Snuck Out with $Subject");                                  // Memory - Family - Sneak Out - First
            MemoryAboutSim.Add(0xF3DF28C4, "Taught $Subject a Nursery Rhyme");                          // Memory - Knowledge - Teach Nursery Rhyme
            MemoryAboutSim.Add(0xCDB133CF, "Taught $Subject to Study");                                 // Memory - Knowledge - Teach to Study
            MemoryAboutSim.Add(0xACAB0305, "Taught $Subject to Talk");                                  // Memory - Knowledge - Teach to Talk
            MemoryAboutSim.Add(0xACAB0FE3, "Taught $Subject to Walk");                                  // Memory - Grow Up - First Teach to Walk
            MemoryAboutSim.Add(0x4F7A4FD3, "Was Attracted to $Subject");                                // Memory - EP2 - Attraction Marker
            MemoryAboutSim.Add(0x4CAB110D, "Was Saved from Death by $Subject");                         // Memory - Knowledge - Near Death Experience
            MemoryAboutSim.Add(0x2CD8D374, "Went Steady with $Subject");                                // Memory - Love - Go Steady
            MemoryAboutSim.Add(0xADE8A617, "Went Steady with $Subject (Fear)");                         // Memory - Love - Go Steady - Fear
            MemoryAboutSim.Add(0xECAB04C2, "Won a Fight with $Subject");                                // Memory - Power - Fight - Win
            MemoryAboutSim.Add(0x8EB8B8AB, "WooHoo with X Different Sims ($Subject)");                  // Memory - Lifetime - Have WooHoo With X Diff Sims

            // Memories which take something which is not a sim as the GUID in data 5-6
            MemoryAboutObject.Clear();

            // Memories which take a career as the GUID in data 5-6
            // ToDo: Handle Work memories
            // MemoryAboutWork.Clear();
            MemoryAboutObject.Add(0x4CAB0EFC, "Got a Big Bonus");                                       // Memory - Wealth - First Big Bonus
            MemoryAboutObject.Add(0xEDB65892, "Got a Job");                                             // Memory - Wealth - Career - Enter Track
            MemoryAboutObject.Add(0x6DB658C7, "Got a Promotion");                                       // Memory - Wealth - Career - Promotion
            MemoryAboutObject.Add(0x2DB65F87, "Got Demoted");                                           // Memory - Wealth - Career - Demoted
            MemoryAboutObject.Add(0x2C8CB6F1, "Got Fired");                                            // Memory - Wealth - Career - Lost Job
            MemoryAboutObject.Add(0x8DB65F9F, "Quit Job");                                              // Memory - Wealth - Career - Quit Job
            MemoryAboutObject.Add(0xCDB65BF7, "Reached Top of the Career Path");                        // Memory - Wealth - Career - Max Level
            MemoryAboutObject.Add(0xCCCE758A, "Retired");                                               // Memory - Wealth - Retirement

            // Memories which take a food as the GUID in data 5-6
            // ToDo: Handle Food memories
            // MemoryAboutFood.Clear();
            MemoryAboutObject.Add(0xEFA2167F, "Ate Grilled Cheese");                                    // Memory - EP2 - Eat Grilled Cheese
            MemoryAboutObject.Add(0xECAB0449, "Burned Food");                                           // Memory - Knowledge - Food - Burn
            MemoryAboutObject.Add(0x2DB66309, "Learned How to Make Food");                              // Memory - Knowledge - Learned to Cook Food Type

            // Memories which take a skill object as the GUID in Data 5-6
            // ToDo: Handle Skill memories
            // MemoryAboutSkill.Clear();
            MemoryAboutObject.Add(0x4DC245B7, "Maximized All Skills");                                  // Memory - Knowledge - Skill - Max All
            MemoryAboutObject.Add(0xEDB673A3, "Maximized the Body Skill");                              // Memory - Knowledge - Skill - Max Body
            MemoryAboutObject.Add(0x8DB673D0, "Maximized the Charisma Skill");                          // Memory - Knowledge - Skill - Max Charisma
            MemoryAboutObject.Add(0x8DB67458, "Maximized the Cleaning Skill");                          // Memory - Knowledge - Skill - Max Cleaning
            MemoryAboutObject.Add(0x4DB672B0, "Maximized the Cooking Skill");                           // Memory - Knowledge - Skill - Max Cooking
            MemoryAboutObject.Add(0x4DB673EF, "Maximized the Creativity Skill");                        // Memory - Knowledge - Skill - Max Creativity
            MemoryAboutObject.Add(0xCDB67440, "Maximized the Logic Skill");                             // Memory - Knowledge - Skill - Max Logic
            MemoryAboutObject.Add(0x2DB67383, "Maximized the Mechanical Skill");                        // Memory - Knowledge - Skill - Max Mechanical
            MemoryAboutObject.Add(0x2EB8B7A4, "Maximized X Skills");                                    // Memory - Lifetime - Max X Skills

            // Memory which takes itself as the GUID in data 5-6
            MemoryAboutObject.Add(0x2CAB0835, "Sold a Masterpiece");                                    // Memory - Wealth - Painting - Sell High
            MemoryAboutObject.Add(0x6CAB0E82, "Vermin!");                                               // Memory - Wealth - Vermin

            // Memory which takes a spaceship (0xCC501689) as the GUID in data 5-6
            MemoryAboutObject.Add(0x2DB66260, "Met Alien (Good)");                                      // Memory - Knowledge - Alien Abduction - Good

            // Memory which takes fire (0x24C95F99) as the GUID in data 5-6
            MemoryAboutObject.Add(0x0CAB0E73, "Fire!");                                                 // Memory - Wealth - Fire

            // Memory which takes genie lamp (0x9413FA7F) as the GUID in data 5-6
            MemoryAboutObject.Add(0x742CE1C5, "Found Magical Lamp");                                    // Memory - EP7 - Genie - Found the Lamp

            // Memories which we have not yet properly categorized
            MemoryAboutUnknown.Clear();

            // Memories which need to be categorized (determine parameter types)
            MemoryAboutUnknown.Add(0xB090D562, "Achieved X Top-Ranked Businesses");                     // Memory - Lifetime - EP3 - Built X Top Ranked Businesses
            MemoryAboutUnknown.Add(0xAFDA8C30, "Ate X Grilled Cheese");                                 // Memory - Lifetime - EP2 - Eat X Grilled Cheese
            MemoryAboutUnknown.Add(0x942CE1F3, "Became Young Again Through Genie Lamp");                // Memory - EP7 - Genie - Became Young Again
            MemoryAboutUnknown.Add(0x0DCA2C8D, "Came Back Home");                                       // Memory - Family - Runaway - Return (Teen)
            MemoryAboutUnknown.Add(0xADCA2BF5, "Got Fined");                                            // Memory - Wealth - Get Fined
            MemoryAboutUnknown.Add(0x2FA2009C, "Got Tray Dropped on Me");                               // Memory - EP2 - Got Food Dumped on
            MemoryAboutUnknown.Add(0xF42BCA6A, "Restored First Car");                                   // Memory - EP7 - Hobby - Tinkering - Restored First Car

            MemoryAboutUnknown.Add(0xCEB89AB5, "Memory - College - Got Expelled");
            MemoryAboutUnknown.Add(0x8EB89EAE, "Memory - EP1 - Golden Anniversary");
            MemoryAboutUnknown.Add(0x305AD45B, "Memory - EP3 - Fear Jack-in-the-Box");
            MemoryAboutUnknown.Add(0x108FBD27, "Memory - EP3 - Robot Ran Amok");
            MemoryAboutUnknown.Add(0xF1A31AEB, "Memory - EP4 - Lost Job");
            MemoryAboutUnknown.Add(0x51A33CCA, "Memory - EP4 - Pet - Ran Away (Pet)");
            MemoryAboutUnknown.Add(0x71A33D9E, "Memory - EP4 - Pet - Returned (Pet)");
            MemoryAboutUnknown.Add(0xF1A31AC5, "Memory - EP4 - Pet Got Demoted");
            MemoryAboutUnknown.Add(0x31A31A3E, "Memory - EP4 - Pet Got Job");
            MemoryAboutUnknown.Add(0xF1A31A75, "Memory - EP4 - Pet Got Promoted");
            MemoryAboutUnknown.Add(0x31A31A9A, "Memory - EP4 - Pet Quit Job");
            MemoryAboutUnknown.Add(0xB1A319C6, "Memory - EP4 - Pet Reached Top of Career");
            MemoryAboutUnknown.Add(0x71A319F0, "Memory - EP4 - Pet Retired");
            MemoryAboutUnknown.Add(0x7343AE20, "Memory - EP6 - Got Pickpocketed");
            MemoryAboutUnknown.Add(0x935EFA34, "Memory - EP6 - Voodoo Doll Backfires");
            MemoryAboutUnknown.Add(0x5436095F, "Memory - EP7 - Badge - Pottery - Gold");
            MemoryAboutUnknown.Add(0x5436096F, "Memory - EP7 - Badge - Sewing - Gold");
            MemoryAboutUnknown.Add(0xD507F555, "Memory - EP8 - NPCs - Learn to Breakdance");
            MemoryAboutUnknown.Add(0x0DB53DD1, "Memory - Template");
            MemoryAboutUnknown.Add(0xEEAFB6D4, "Memory - Wealth - Counterfeiting - Busted");
            MemoryAboutUnknown.Add(0x8EAFBE5F, "Memory - Wealth - Counterfeiting - Get Fined");

            // Memory Items which have an inventory number, or known objects
            InventoryItem.Clear();
// #if DEBUG
            // ToDo: Re-enable for RELEASE if we ever need to check validity
            InventoryItem.Add(0xB3FEC1B6, "Audio Augmenter by Little Mole Inc.");                       // Career - Surveillance Mic
            InventoryItem.Add(0xCC58DF85, "AquaGreen Hydroponic Garden");                               // Career - Hydroponic Garden
            InventoryItem.Add(0x8E8D529F, "Cozmo MP3 Player");                                          // MP3 Player
            InventoryItem.Add(0x90900145, "Daisy Bouquet");                                             // Flower - Craftable - Daisy Bouquet 
            InventoryItem.Add(0x4E9FBE5D, "Dr. Vu's Automated Cosmetic Surgeon");                       // Career - Home Plastic Surgery Kit
            InventoryItem.Add(0xAC314A3A, "Enterprise Office Concepts Bushmaster Tele-Prompter");       // Career - Teleprompter
            InventoryItem.Add(0xCC20426A, "Execuputter");                                               // Career - Putting Green
            InventoryItem.Add(0x4C2148B0, "Exerto Punching Bag");                                       // Career - Punching Bag
            InventoryItem.Add(0x6C2979FB, "Exerto Selfflog Obstacle Course");                           // Career - Obstacle Course
            InventoryItem.Add(0x522D55DC, "Fish - Raw - Bass");
            InventoryItem.Add(0x725E1F4D, "Fish - Raw - Bass - Small");
            InventoryItem.Add(0xB22ED3DA, "Fish - Raw - Boot");
            InventoryItem.Add(0x522ED013, "Fish - Raw - Catfish");
            InventoryItem.Add(0xB25E1F7C, "Fish - Raw - Catfish - Small");
            InventoryItem.Add(0x122ED3AE, "Fish - Raw - Golden Trout");
            InventoryItem.Add(0xB22ED0C9, "Fish - Raw - Rainbow Trout");
            InventoryItem.Add(0x525E1FA1, "Fish - Raw - Rainbow Trout - Small");
            InventoryItem.Add(0xEF2B31B0, "\"Gastronomique\" Restaurant Podium");                       // Dining - Podium
            InventoryItem.Add(0xCEA505BB, "Laganaphyllis Simnovorii");                                  // Career - Cow Plant
            InventoryItem.Add(0xAEB9F591, "Luminous Pro Antique Camera");                               // Career - Camera
            InventoryItem.Add(0x53268827, "Map - Secret Lot - Far East");
            InventoryItem.Add(0xF3267805, "Map - Secret Lot - Mountain");
            InventoryItem.Add(0xF3268844, "Map - Secret Lot - Tropics");
            InventoryItem.Add(0xF3883B5E, "Painting - Gold Doubloon");
            InventoryItem.Add(0x742509EC, "Plaque of Arts and Crafts");                                 // Painting - Hobby - Arts and Crafts Plaque
            InventoryItem.Add(0x34250BC7, "Plaque of Cuisine");                                         // Painting - Hobby - Cuisine Plaque
            InventoryItem.Add(0x34250BD3, "Plaque of Film and Literature");                             // Painting - Hobby - Film and Literature Plaque
            InventoryItem.Add(0x54250C19, "Plaque of Fitness");                                         // Painting - Hobby - Fitness Plaque
            InventoryItem.Add(0x54250BDF, "Plaque of Games");                                           // Painting - Hobby - Games Plaque
            InventoryItem.Add(0x14250C23, "Plaque of Music and Dance");                                 // Painting - Hobby - Music and Dance Plaque
            InventoryItem.Add(0x34250C0F, "Plaque of Nature");                                          // Painting - Hobby - Nature Plaque
            InventoryItem.Add(0xB4250BF9, "Plaque of Science");                                         // Painting - Hobby - Science Plaque
            InventoryItem.Add(0xD4250C06, "Plaque of Sports");                                          // Painting - Hobby - Sports Plaque
            InventoryItem.Add(0xD4250BEF, "Plaque of Tinkering");                                       // Painting - Hobby - Tinkering Plaque
            InventoryItem.Add(0xD09A048D, "Podium of Bonnappitizon");                                   // Dining - Podium - International 
            InventoryItem.Add(0xD1CD15C8, "Prints Charming Fingerprinting Scanner");                    // Career - Fingerprint Kit
            InventoryItem.Add(0xAFA8949C, "ReNuYu Porta-Chug");                                         // Potion - Turn Off/On
            InventoryItem.Add(0xAE8D50B2, "The Resurrect-O-Nomitron");                                  // Career - Resurrectonomitron
            InventoryItem.Add(0x8EE9B9CC, "\"Save the Sheep\" Faux Sheepskin Diploma");                 // Painting - College - Diploma
            InventoryItem.Add(0x8C4D2997, "Schokolade 890 Chocolate Manufacturing Facility");           // Career - Candy Factory
            InventoryItem.Add(0xF354F267, "Sculpture - Captains Log");
            InventoryItem.Add(0x336890F3, "Sculpture - Dragon Scroll");
            InventoryItem.Add(0xCC16D816, "SensoTwitch Lie Finder");                                    // Career - Polygraph
            InventoryItem.Add(0x0C6E194A, "Simsanto Inc. Biotech Station");                             // Career - Biotech Station
            InventoryItem.Add(0x6C6CE31F, "TraumaTime \"Incision Precision\" Surgical Training Station"); // Career - Surgical Dummy
            InventoryItem.Add(0xF354E8CB, "Treasure Map");                                              // Painting - Treasure Map
            InventoryItem.Add(0xB3281ED8, "Voodoo Doll");

            InventoryItem.Add(0x33EC6E0A, "Career - Ballet Bar");
            InventoryItem.Add(0x524E1066, "Career - Bookcase");
            InventoryItem.Add(0xB3FD5372, "Career - Drafting Table");
            InventoryItem.Add(0x33E2E4E8, "Career - Fame Star Rug");
            InventoryItem.Add(0xD24D09FD, "Career - Golden Skull of Jumbok IV");
            InventoryItem.Add(0x324D0D87, "Career - Guitar");
            InventoryItem.Add(0xD24CE39C, "Career - Journalism Award");
            InventoryItem.Add(0x53EDA12F, "Career - Koi Pond");
            InventoryItem.Add(0x124E3138, "Career - Lectern");
            InventoryItem.Add(0xF24CFF80, "Career - Pinball");
            InventoryItem.Add(0x2B55B9F8, "Computer - Cheap");
            InventoryItem.Add(0x6C81E133, "Computer - Expensive");
            InventoryItem.Add(0xB0626C4C, "Deed - Commercial Lot");
// #endif

            // Tokens: Tokens and Controllers which do not have the Memory flag set, and which do not have an inventory number
            ItemNotMemory.Clear();
            ItemNotMemory.Add(0xB4DBFC33, "$Subject is an Apartment Resident");                         // Token - Apartment NPC Resident
            ItemNotMemory.Add(0xED4CB1A4, "Pregnant by $Subject");                                      // Controller - Pregnancy
            ItemNotMemory.Add(0xAFA0DE64, "Don't Mess With Romantic Rival $Subject");                   // Token - Don't Mess With Romantic Rival
            ItemNotMemory.Add(0x2CBFC3EF, "$NPCType $Subject is Banished from Lot");                    // Token - NPC - Banished From Lot
            ItemNotMemory.Add(0x4CB2BCF7, "$NPCType $Subject is Familiar with Lot");                    // Token - NPC - Familiar to Lot
            ItemNotMemory.Add(0xCF505AB0, "Furious at $Subject");                                       // Token - Furious
            ItemNotMemory.Add(0xEFED8EC1, "$Subject is Furious");                                       // Token - Furious Reverse
            ItemNotMemory.Add(0x8E77E373, "Has Door Key for Lot: $Lot");                                // Token - Door Key
            ItemNotMemory.Add(0x0EAA2454, "Member of Greek House: $Lot");                               // Token - College - Frat Member
            ItemNotMemory.Add(0xED2D4357, "NPC $Sim1 was Hired By $Sim2");                              // Token - NPC - Preselected
            ItemNotMemory.Add(0x0C9BF0FB, "Received an Inheritance from $Subject");                     // Token - Inheritance
            ItemNotMemory.Add(0xCF6FF511, "Tombstone for $Subject is being moved to $Lot in $Hood");    // Token - UrnStone
// #if DEBUG
            // ToDo: Re-enable for RELEASE if we ever need to check validity
            ItemNotMemory.Add(0xCC6633B5, "Accessory - Clothes");
            ItemNotMemory.Add(0x8D0C081F, "Controller - Game Scripting");
            ItemNotMemory.Add(0x9E1572E3, "Dance - Rave - Couples");
            ItemNotMemory.Add(0x6C7EEEF4, "Disease - Token");
            ItemNotMemory.Add(0x4C92F505, "Family Aspiration");                                         // Aspiration - Family
            ItemNotMemory.Add(0xB40F9E9A, "First Place Food Contest Ribbon");                           // Sculpture - Blue Ribbon
            ItemNotMemory.Add(0x4C92F480, "Fortune Aspiration");                                        // Aspiration - Wealth
            ItemNotMemory.Add(0x505FDC28, "Get a Cash Register Talent Badge");                          // Token - Badge - Cash Register
            ItemNotMemory.Add(0x705FDC8F, "Get a Cosmetology Talent Badge");                            // Token - Badge - Cosmetology
            ItemNotMemory.Add(0x12297157, "Get a Fishing Talent Badge");                                // Token - Badge - Fishing
            ItemNotMemory.Add(0x905FDBB5, "Get a Flower Arranging Talent Badge");                       // Token - Badge - Flower Arranging
            ItemNotMemory.Add(0x1215A8C1, "Get a Gardening Talent Badge");                              // Token - Badge - Gardening
            ItemNotMemory.Add(0xF3D9B8DA, "Get a Pottery Talent Badge");                                // Token - Badge - Pottery
            ItemNotMemory.Add(0x105FDC52, "Get a Restocking Talent Badge");                             // Token - Badge - Stocking
            ItemNotMemory.Add(0x505FDBC6, "Get a Robotics Talent Badge");                               // Token - Badge - Robotery
            ItemNotMemory.Add(0xB05FDC1B, "Get a Sales Talent Badge");                                  // Token - Badge - Sales
            ItemNotMemory.Add(0xD3DB2A27, "Get a Bronze Sewing Talent Badge");                          // Token - Badge - Sewing
            ItemNotMemory.Add(0xD05FDB91, "Get a Toy Making Talent Badge");                             // Token - Badge - Toy Making
            ItemNotMemory.Add(0xAC8F36D1, "Grow Up Aspiration");                                        // Aspiration - Power
            ItemNotMemory.Add(0x6C92F4CF, "Knowledge Aspiration");                                      // Aspiration - Knowledge
            ItemNotMemory.Add(0x8C92F4BC, "Popularity Aspiration");                                     // Aspiration - Reputation
            ItemNotMemory.Add(0x6C92F4F3, "Romance Aspiration");                                        // Aspiration - Romance
            ItemNotMemory.Add(0x94D80DBA, "Token - Anger Management Studied");
            ItemNotMemory.Add(0x14602CE5, "Token - Apartment - Object Copies");
            ItemNotMemory.Add(0xD4FFE903, "Token - Apartment Landlord");
            ItemNotMemory.Add(0x34C6E493, "Token - Apartment Resident");
            ItemNotMemory.Add(0x350FB715, "Token - Apartment Townies - Friends");
            ItemNotMemory.Add(0x2F4D6811, "Token - Attraction Marker");
            ItemNotMemory.Add(0x54E2ABA5, "Token - Break Dance Skill");
            ItemNotMemory.Add(0xF08B570B, "Token - Business - Need Help");
            ItemNotMemory.Add(0xF0A1D79F, "Token - Business - Work Outfit Index - Lot");
            ItemNotMemory.Add(0x9552F70C, "Token - Butler");
            ItemNotMemory.Add(0x6C8EEF7F, "Token - Call In Sick");
            ItemNotMemory.Add(0x0ECB23E7, "Token - College Lot Existance");
            ItemNotMemory.Add(0x2EAA244C, "Token - College - Pledge");
            ItemNotMemory.Add(0x4ECDB7A1, "Token - College - Scholarship");
            ItemNotMemory.Add(0x8EAE367E, "Token - College - Secret Society");
            ItemNotMemory.Add(0x6EA8FB5D, "Token - College Townie Age Now");
            ItemNotMemory.Add(0x54D36CBD, "Token - Community Lot - Already Greeted");
            ItemNotMemory.Add(0x8FCD5705, "Token - Contact");
            ItemNotMemory.Add(0x34D358B7, "Token - Couple Counseling Studied");
            ItemNotMemory.Add(0x6FE7E453, "Token - Dance Experience");
            ItemNotMemory.Add(0x0DA265F4, "Token - Dance Skill");
            ItemNotMemory.Add(0x12F49912, "Token - Dance - Hula");
            ItemNotMemory.Add(0x932BC5B5, "Token - Dance - Slap");
            ItemNotMemory.Add(0x907BAD69, "Token - Dining Loyalty");
            ItemNotMemory.Add(0xEF5D722E, "Token - Dining Now");
            ItemNotMemory.Add(0x0D6B386C, "Token - Disease Immunity");
            ItemNotMemory.Add(0x70B849C7, "Token - Employee - Leave Work");
            ItemNotMemory.Add(0x54475C5C, "Token - Family - BFF Initialized");
            ItemNotMemory.Add(0xB4503567, "Token - Family - EP7 Secret Hobby Lot Visited");
            ItemNotMemory.Add(0x334600E0, "Token - Fire Dance Skill");
            ItemNotMemory.Add(0x74D80DA8, "Token - Fire Prevention Studied");
            ItemNotMemory.Add(0xD3634D0A, "Token - Found - Secret Lot - BigFoot");
            ItemNotMemory.Add(0x13634CE9, "Token - Found - Secret Lot - Hermit");
            ItemNotMemory.Add(0x93634D2C, "Token - Found - Secret Lot - WitchDoctor");
            ItemNotMemory.Add(0xF21D8A62, "Token - Garden Club Member");
            ItemNotMemory.Add(0x325C9CC9, "Token - Garden Club - Visited");
            ItemNotMemory.Add(0xF38C73E0, "Token - Good Vacations Count");
            ItemNotMemory.Add(0x1438C1DF, "Token - Global - BFF Initialized");
            ItemNotMemory.Add(0xAE82B295, "Token - GPA");
            ItemNotMemory.Add(0x93228484, "Token - Greet - Learned Far East");
            ItemNotMemory.Add(0x1322849B, "Token - Greet - Learned Island");
            ItemNotMemory.Add(0x73228492, "Token - Greet - Learned Mountain");
            ItemNotMemory.Add(0x50A09BA6, "Token - Gypsy - Visited");
            ItemNotMemory.Add(0xF444B84F, "Token - Has Own Bug Box");
            ItemNotMemory.Add(0x74000ECE, "Token - Hobby - Decay");
            ItemNotMemory.Add(0x33F6E0FB, "Token - Hobby - Initialized");
            ItemNotMemory.Add(0x53F9DF4E, "Token - Hobby - Membership");
            ItemNotMemory.Add(0xF4DBFB86, "Token - Hobby - Phone Call TNS Shown");
            ItemNotMemory.Add(0x94644934, "Token - Hobby TNS Shown - Blog on Computer");
            ItemNotMemory.Add(0x7464492B, "Token - Hobby TNS Shown - Browse on Computer");
            ItemNotMemory.Add(0x7464493C, "Token - Hobby TNS Shown - In the Zone");
            ItemNotMemory.Add(0xF4FFFA5C, "Token - Hobby TNS Shown - Instruct In Hobby");
            ItemNotMemory.Add(0x14644943, "Token - Hobby TNS Shown - Read Paper");
            ItemNotMemory.Add(0x3464494A, "Token - Hobby TNS Shown - Share Tips");
            ItemNotMemory.Add(0x54644665, "Token - Hobby TNS Shown - Talk About Hobby");
            ItemNotMemory.Add(0x5350EC38, "Token - Hotel Key");
            ItemNotMemory.Add(0x2CCF9BED, "Token - I Am Dead");
            ItemNotMemory.Add(0x531FE710, "Token - I am Ninja!");
            ItemNotMemory.Add(0xEF54615A, "Token - Late For Work");
            ItemNotMemory.Add(0xF317FDFD, "Token - Learned Dragon Legend");
            ItemNotMemory.Add(0x52E9E443, "Token - Learned Massage Acupressure");
            ItemNotMemory.Add(0x92D3596F, "Token - Learned Massage Deep Tissue");
            ItemNotMemory.Add(0x32E9E44C, "Token - Learned Massage Hot Stone");
            ItemNotMemory.Add(0x130D8CA1, "Token - Learned Sea Shanty");
            ItemNotMemory.Add(0x54D80DC6, "Token - Lifelong Happiness Studied");
            ItemNotMemory.Add(0x2CD9022F, "Token - Lot - Food Unit Info");
            ItemNotMemory.Add(0xF0ADD9D8, "Token - Lot Occupied");
            ItemNotMemory.Add(0xB0B2BA1E, "Token - Lot - Reset CLP");
            ItemNotMemory.Add(0x4DD398A5, "Token - Lot Export - Primary ");
            ItemNotMemory.Add(0x73F2D0A3, "Token - LTA Direct Adds");
            ItemNotMemory.Add(0xB424F345, "Token - LTA - LTA Inited");
            ItemNotMemory.Add(0x33E355C0, "Token - LTA Superpowers");
            ItemNotMemory.Add(0xB44106D9, "Token - Magazine Subscription - Family Offered Subscription");
            ItemNotMemory.Add(0x4D8B0CC3, "Token - Misc Skill");
            ItemNotMemory.Add(0xB3F19C26, "Token - Motive Decay Modifier");
            ItemNotMemory.Add(0xACE48C06, "Token - Motive Transport");
            ItemNotMemory.Add(0xEDE5D047, "Token - Move In");
            ItemNotMemory.Add(0xACDE44B2, "Token - Move Out");
            ItemNotMemory.Add(0x146C50A5, "Token - Neighborhood - Bad Pickpockets");
            ItemNotMemory.Add(0x4CB4FDC6, "Token - NPC - On The Job");
            ItemNotMemory.Add(0xEF5079E1, "Token - On Outing/Date");
            ItemNotMemory.Add(0x908B36FF, "Token - Pass Rewards Knowledge");
            ItemNotMemory.Add(0x2CEB51AC, "Token - Personal Wealth");
            ItemNotMemory.Add(0x6D5C955C, "Token - Pregnancy - Modifier");
            ItemNotMemory.Add(0x53FEA775, "Token - Promotion Requires One Less Friend");
            ItemNotMemory.Add(0xB3F2D735, "Token - Psychic Parent");
            ItemNotMemory.Add(0x54D80DD3, "Token - Physiology Studied");
            ItemNotMemory.Add(0xECD9F507, "Token - Public School - Grade Count");
            ItemNotMemory.Add(0x108F47DF, "Token - Remote Business Data");
            ItemNotMemory.Add(0x54C0BEB3, "Token - Reputation");
            ItemNotMemory.Add(0x5450598C, "Token - Rod Humble - Visited");
            ItemNotMemory.Add(0x53D08989, "Token - Secondary Aspiration");
            ItemNotMemory.Add(0x74D328A8, "Token - Secret Network - Cheap Catalog");
            ItemNotMemory.Add(0x8DD72D13, "Token - Sim - Foods Cooked Well");
            ItemNotMemory.Add(0x2E016316, "Token - Sim - Foods Eaten");
            ItemNotMemory.Add(0x0CB3F2B3, "Token - Sim - Initialized");
            ItemNotMemory.Add(0x8E042055, "Token - Sim - Loaded");
            ItemNotMemory.Add(0x4DA3D8DD, "Token - Sleep - Pajamas");
            ItemNotMemory.Add(0x6DA3D91A, "Token - Sleep - Underwear");
            ItemNotMemory.Add(0x541902EA, "Token - STA Motive Benefits Weekly");
            ItemNotMemory.Add(0x7332278C, "Token - Tai Chi Skill");
            ItemNotMemory.Add(0x9237DB93, "Token - Temperature - Cold");
            ItemNotMemory.Add(0xD237DB5E, "Token - Temperature - Freeze");
            ItemNotMemory.Add(0x3237DB4B, "Token - Temperature - Sunburn");
            ItemNotMemory.Add(0x2E83E2AD, "Token - Term Paper");
            ItemNotMemory.Add(0xF07B919E, "Token - Timestamp");
            ItemNotMemory.Add(0x4DDF0E12, "Token - Toddler Skill Token");
            ItemNotMemory.Add(0xF0A9D3DD, "Token - Townie - Badge Seeded");
            ItemNotMemory.Add(0x13219011, "Token - Traveled to Far East");
            ItemNotMemory.Add(0x53219060, "Token - Traveled to Islands");
            ItemNotMemory.Add(0x73219021, "Token - Traveled to Mountains");
            ItemNotMemory.Add(0x0FB6DA80, "Token - UrnStone - Visible Inventory Tracker");
            ItemNotMemory.Add(0x334F2151, "Token - Vacation Benefits - Generic");
            ItemNotMemory.Add(0x93A2672D, "Token - Vacation Sim - Local Initialized");
            ItemNotMemory.Add(0xF1AD3E9A, "Token - Walk By - With Pet");
            ItemNotMemory.Add(0x524B9BAD, "Token - Weather - Community Lot Compatibility");
            ItemNotMemory.Add(0xB3635410, "Token - Went on Tour - FarEast - Bamboo Forest");
            ItemNotMemory.Add(0x136353F4, "Token - Went on Tour - FarEast - Historic Walking Tour");
            ItemNotMemory.Add(0x93635405, "Token - Went on Tour - FarEast - River Boat");
            ItemNotMemory.Add(0x53635323, "Token - Went on Tour - Mountain - Bird Watching");
            ItemNotMemory.Add(0xD3635308, "Token - Went on Tour - Mountain - Logging Expedition");
            ItemNotMemory.Add(0x1363533E, "Token - Went on Tour - Mountain - Nature Trail");
            ItemNotMemory.Add(0x136353B4, "Token - Went on Tour - Tropical - Glass bottom boat");
            ItemNotMemory.Add(0x536353C7, "Token - Went on Tour - Tropical - Helicopter Tour");
            ItemNotMemory.Add(0xF36353D8, "Token - Went on Tour - Tropical - Parasailing");
            ItemNotMemory.Add(0x74ACBAF4, "Token - Witch Token");
            ItemNotMemory.Add(0xAD2EA3EF, "VideoGame - CD - Bustin Out");
            ItemNotMemory.Add(0xF3F55F08, "VideoGame - CD - NewGame2");
            ItemNotMemory.Add(0xAD2EA422, "VideoGame - CD - Sim City 4");
            ItemNotMemory.Add(0x0D26B746, "VideoGame - CD - SSX3 (Default Game)");
            ItemNotMemory.Add(0xB45C3B7A, "VideoGame - CD - The Sims 3");
// #endif

            // Ensure that memories are unique
#if DEBUG
            ICollection<uint> AllMemories = new List<uint>();
            foreach (uint key in MemoryAboutSelf.Keys)
                AllMemories.Add(key);
            foreach (uint key in MemoryAboutSim.Keys)
            {
                Debug.Assert(!AllMemories.Contains(key));
                AllMemories.Add(key);
            }
            foreach (uint key in MemoryAboutObject.Keys)
            {
                Debug.Assert(!AllMemories.Contains(key));
                AllMemories.Add(key);
            }
            foreach (uint key in MemoryAboutUnknown.Keys)
            {
                Debug.Assert(!AllMemories.Contains(key));
                AllMemories.Add(key);
            }
            foreach (uint key in InventoryItem.Keys)
            {
                Debug.Assert(!AllMemories.Contains(key));
                AllMemories.Add(key);
            }
            foreach (uint key in ItemNotMemory.Keys)
            {
                Debug.Assert(!AllMemories.Contains(key));
                AllMemories.Add(key);
            }
#endif
        }
    }
}


