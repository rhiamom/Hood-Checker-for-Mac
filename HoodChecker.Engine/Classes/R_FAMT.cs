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
using System.Diagnostics;
using SimPe.Interfaces.Files;

namespace LotExpander
{
    // FAMT - Family Ties
    public class R_FAMT
    {
#if DEBUG
        private bool Test_PrintAllTies = false;     // Enable (T) or disable (F) printing of all family ties
#endif

        private IHoodCheckContext fParent;
        private bool bRemoveUnknown = false;
        private bool bHoodFileChanged = false;
        private string sFixed = "";

        public R_FAMT(IHoodCheckContext fp)
        {
            fParent = fp;
        }

        public bool CheckAllFamilyTies(bool bFix)
        {
            bRemoveUnknown = bFix;
            sFixed = (bRemoveUnknown) ? "Removed: " : "";

            IPackedFileDescriptor[] FamilyTies = fParent.NBPack.FindFiles(0x8C870743);
            Debug.Assert(1 == FamilyTies.Length);
            fParent.AddToList("Family Ties:");
            foreach (IPackedFileDescriptor Ties in FamilyTies)
                CheckFamilyTiesRecord(Ties);
            return bHoodFileChanged;
        }

        private void CheckFamilyTiesRecord(IPackedFileDescriptor Ties)
        {
            IPackedFile PF = fParent.NBPack.Read(Ties);
            byte[] Data = PF.UncompressedData;
            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);
            byte[] DataNew = new byte[Data.Length];
            BinaryWriter BW = new BinaryWriter(new MemoryStream(DataNew));

            int iBlockVersion1 = BR.ReadInt32();
            Debug.Assert(iBlockVersion1 == 1);
            // ToDo: Determine whether other versions are known and handled correctly
            BW.Write(iBlockVersion1);

            int iSimCount = BR.ReadInt32();
            int iSimCountNew = 0;
            long iSimCountIndex = BW.BaseStream.Position;
            BW.Write(iSimCount);
            fParent.IncreaseWorkload(iSimCount);
            for (int i = 0; i < iSimCount; i++)
            {
                if (CheckFamilyTiesForSim(BR, BW))
                    iSimCountNew++;
                fParent.MadeProgress();
            }
            if (bRemoveUnknown)
            {
                long iIndex = BW.BaseStream.Position;
                if( iSimCountNew != iSimCount)
                {
                    // Fix the number of Sims with Family Ties.
                    BW.BaseStream.Position = iSimCountIndex;
                    BW.Write(iSimCountNew);
                    BW.BaseStream.Position = iIndex;
                }
                if( iIndex != Data.Length)
                {
                    bHoodFileChanged = true;
                    
                    // Truncate to new size and write record.
                    byte[] DataTruncate = new byte[iIndex];
                    Array.Copy(DataNew, DataTruncate, iIndex);

                    Data = DataTruncate;
                    Ties.SetUserData(Data, true);
                }
            }
        }

        private string[] sFamilyTies =
        {
            /*  0 */ "first parent",
            /*  1 */ "second parent",
            /*  2 */ "spouse",
            /*  3 */ "sibling",
            /*  4 */ "child"
        };

        private bool CheckFamilyTiesForSim(BinaryReader BR, BinaryWriter BW)
        {
            int iParentA = 0;
            int iParentB = 0;
            int iSpouse = 0;

            ushort uSubjectInstance = BR.ReadUInt16();
            bool bSubjectExists = fParent.SimNameFromInstance.ContainsKey(uSubjectInstance);
            string sSubjectName = (bSubjectExists) ? fParent.SimNameFromInstance[uSubjectInstance] : null;
            if (bSubjectExists)
                BW.Write(uSubjectInstance);

            int iBlockVersion2 = BR.ReadInt32();
            Debug.Assert(iBlockVersion2 == 1);
            // ToDo: Determine whether other versions are known and handled correctly
            if (bSubjectExists)
                BW.Write(iBlockVersion2);

            int iTieCount = BR.ReadInt32();
            int iTieCountNew = 0;
            long iTieCountIndex = BW.BaseStream.Position;
            if (bSubjectExists)
                BW.Write(iTieCount);

            if (0 < iTieCount)
            {
                for (int i = 0; i < iTieCount; i++)
                {
                    int iTieType = BR.ReadInt32();
                    Debug.Assert((0 <= iTieType) && (iTieType < sFamilyTies.Length));
                    if (iTieType == 0)
                        iParentA++;
                    else if (iTieType == 1)
                        iParentB++;
                    else if (iTieType == 2)
                        iSpouse++;

                    if (CheckOneFamilyTie(BR, BW, uSubjectInstance, iTieType))
                        iTieCountNew++;
                }
            }
            else // if (0 == iTieCount)
            #region Log Sim With No Family Ties
            {
                if (bSubjectExists)
                {
#if DEBUG
                    if (Test_PrintAllTies && !bRemoveUnknown)
                        fParent.AddToList(string.Format("  Valid: {0} has no family ties", sSubjectName));
#endif
                }
                else
                    fParent.AddToList(string.Format("  {0}Sim does not exist: 0x{1:X4} has no family ties", sFixed, uSubjectInstance));
            }
            #endregion  // Log Sim With No Family Ties

            #region Log Valid Sim With Too Many Family Ties
            if (bSubjectExists)
            {
                if (iParentA > 1)
                    fParent.AddToList(string.Format("  {0} has {1} first parents", sSubjectName, iParentA));
                if (iParentB > 1)
                    fParent.AddToList(string.Format("  {0} has {1} second parents", sSubjectName, iParentB));
                if (iSpouse > 1)
                    fParent.AddToList(string.Format("  {0} has {1} spouses", sSubjectName, iSpouse));
            }
            #endregion // Log Valid Sim With Too Many Family Ties

            if (bRemoveUnknown)
            {
                if (iTieCountNew != iTieCount)
                {
                    // Fix the number of Family Ties for this sim.
                    long iIndex = BW.BaseStream.Position;
                    BW.BaseStream.Position = iTieCountIndex;
                    BW.Write(iTieCountNew);
                    BW.BaseStream.Position = iIndex;
                }
            }
            return bSubjectExists;
        }

        private bool CheckOneFamilyTie(BinaryReader BR, BinaryWriter BW, ushort uSubjectInstance, int iTieType)
        {
            bool bValidTie = false;
            string sTieName = sFamilyTies[iTieType];

            bool bSubjectExists = fParent.SimNameFromInstance.ContainsKey(uSubjectInstance);
            string sSubjectName = (bSubjectExists) ? fParent.SimNameFromInstance[uSubjectInstance] : null;

            ushort uTargetInstance = BR.ReadUInt16();
            bool bTargetExists = fParent.SimNameFromInstance.ContainsKey(uTargetInstance);
            string sTargetName = (bTargetExists) ? fParent.SimNameFromInstance[uTargetInstance] : null;

            if (bSubjectExists)
            {
                if (bTargetExists && (uSubjectInstance != uTargetInstance))
                {
                    bValidTie = true;
                    BW.Write(iTieType);
                    BW.Write(uTargetInstance);
                    #region Log Valid Family Tie
#if DEBUG
                    if (Test_PrintAllTies && !bRemoveUnknown)
                        fParent.AddToList(string.Format("  Valid: {0} has {1} {2}",
                            sSubjectName, sTieName, sTargetName));
#endif
                    #endregion // Log Valid Family Tie
                }
                else
                #region Log Valid Sim With Invalid Family Ties
                {
                    if (uSubjectInstance == uTargetInstance)
                        fParent.AddToList(string.Format("  {0}Family tie with self: {1} has self as {2}",
                            sFixed, sSubjectName, sTieName));
                    else // if (!bTargetExists)
                        fParent.AddToList(string.Format("  {0}Second sim does not exist: {1} has {2} 0x{3:X4}",
                            sFixed, sSubjectName, sTieName, uTargetInstance));
                }
                #endregion // Log Valid Sim With Invalid Family Ties
            }
            else // (!bSubjectExists)
            #region Log Invalid Sim With Family Ties
            {
                if (uSubjectInstance == uTargetInstance)
                    fParent.AddToList(string.Format("  {0}Sim does not exist: 0x{1:X4} has self as {2}",
                        sFixed, uSubjectInstance, sTieName));
                else if (bTargetExists)
                    fParent.AddToList(string.Format("  {0}First sim does not exist: 0x{1:X4} has {2} {3}",
                        sFixed, uSubjectInstance, sTieName, sTargetName));
                else
                    fParent.AddToList(string.Format("  {0}Neither sim exists: 0x{1:X4} has {2} 0x{3:X4}",
                        sFixed, uSubjectInstance, sTieName, uTargetInstance));
            }
            #endregion // Log Invalid Sim With Family Ties

            return bValidTie;
        }
    }
}


