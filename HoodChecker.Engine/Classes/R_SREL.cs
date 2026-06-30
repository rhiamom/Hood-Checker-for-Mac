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
    public class R_SREL
    {
#if DEBUG
        private bool Test_PrintAllRelations = false;    // Enable (T) or disable (F) printing of all relationships
#endif

        private IHoodCheckContext fParent;
        private bool bRemoveUnknown = false;
        private bool bHoodFileChanged = false;
        private string sFixed = "";

        public R_SREL(IHoodCheckContext fp)
        {
            fParent = fp;
        }

        public bool CheckAllSimRelations(bool bFix)
        {
            bRemoveUnknown = bFix;
            sFixed = (bRemoveUnknown) ? "Removed: " : "";

            fParent.AddToList("Sim Relations:");

            // SREL - Sim Relations
            IPackedFileDescriptor[] SimRelations = fParent.NBPack.FindFiles(0xCC364C2A);
            fParent.IncreaseWorkload(SimRelations.Length);
            foreach (IPackedFileDescriptor Relationship in SimRelations)
            {
                if (!IsValidSimRelation(Relationship))
                    if (bRemoveUnknown)
                    {
                        bHoodFileChanged = true;
                        fParent.NBPack.Remove(Relationship);
                    }
                fParent.MadeProgress();
            }
            return bHoodFileChanged;
        }

        private bool IsValidSimRelation(IPackedFileDescriptor Relationship)
        {
            bool bValidRelation = false;

            ushort uSubjectInstance = (ushort)((Relationship.Instance & 0xFFFF0000) >> 16);
            bool bSubjectExists = fParent.SimNameFromInstance.ContainsKey(uSubjectInstance);
            string sSubjectName = (bSubjectExists) ? fParent.SimNameFromInstance[uSubjectInstance] : null;

            ushort uTargetInstance = (ushort)(Relationship.Instance & 0x0000FFFF);
            bool bTargetExists = fParent.SimNameFromInstance.ContainsKey(uTargetInstance);
            string sTargetName = (bTargetExists) ? fParent.SimNameFromInstance[uTargetInstance] : null;

            if (bSubjectExists)
            {
                if (fParent.SelfRelationsValid && (uSubjectInstance == uTargetInstance))
                    bValidRelation = IsValidSelfRelation(Relationship, sSubjectName);
                else if (bTargetExists && (uSubjectInstance != uTargetInstance))
                {
                    bValidRelation = true;
                    #region Log Valid Relation
#if DEBUG
                    if (Test_PrintAllRelations && !bRemoveUnknown)
                        fParent.AddToList(string.Format("  Valid: {0} has relationship with {1}",
                            sSubjectName, sTargetName));
#endif
                    #endregion // Log Valid Relation
                }
                else
                #region Log Valid Sim With Invalid Relations
                {
                    if (uSubjectInstance == uTargetInstance)
                        fParent.AddToList(string.Format("  {0}Relationship with self: {1}", sFixed, sSubjectName));
                    else // if (!bTargetExists)
                        fParent.AddToList(string.Format("  {0}Second sim does not exist: {1} has relationship with 0x{2:X4}",
                            sFixed, sSubjectName, uTargetInstance));
                }
                #endregion // Log Valid Sim With Invalid Relation
            }
            else // (!bSubjectExists)
            #region Log Invalid Sim with Relations
            {
                if (uSubjectInstance == uTargetInstance)
                    fParent.AddToList(string.Format("  {0}Sim does not exist: 0x{1:X4} has relationship with self",
                        sFixed, uSubjectInstance));
                else if (bTargetExists)
                    fParent.AddToList(string.Format("  {0}First sim does not exist: 0x{1:X4} has relationship with {2}",
                        sFixed, uSubjectInstance, sTargetName));
                else
                    fParent.AddToList(string.Format("  {0}Neither sim exists: 0x{1:X4} has relationship with 0x{2:X4}: ",
                        sFixed, uSubjectInstance, uTargetInstance));
            }
            #endregion // Log Invalid Sim with Relations
            return bValidRelation;
        }

        private bool IsValidSelfRelation(IPackedFileDescriptor Relationship, string sSubjectName)
        {
            bool bValidRelation = true;

            IPackedFile PF = fParent.NBPack.Read(Relationship);
            byte[] Data = PF.UncompressedData;
            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);

            int iVersion = BR.ReadInt32();
            Debug.Assert(iVersion == 2);

            int iCount = BR.ReadInt32();
            for (int i = 0; i < iCount; i++)
            {
                uint uData = BR.ReadUInt32();
                if (0 == uData)
                    continue;
                bValidRelation = false;
                break;
            }

            if (bValidRelation)
            #region Log Valid Self-Relation
            {
#if DEBUG
                if (Test_PrintAllRelations && !bRemoveUnknown)
                    fParent.AddToList(string.Format("  Valid: {0} has relationship with self", sSubjectName));
#endif
            }
            #endregion // Log Valid Self-Relation
            else
                fParent.AddToList(string.Format("  {0}Invalid relationship with self: {1}", sFixed, sSubjectName));
            return bValidRelation;
        }
    }
}


