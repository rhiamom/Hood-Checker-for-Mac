/***************************************************************************
 *   Copyright (C) 2010 by Mootilda                                        *
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SimPe.Interfaces.Files;

namespace LotExpander
{
    public class R_FAMI
    {
        private bool Test_PrintDebugInfo = false;   // Enable (T) or disable (F) printing of debug information

        private IPackedFileDescriptor PFD;
        private byte[] Data;
        private uint uLotInstance = 0;

        public R_FAMI(IPackageFile NBPackage, IPackedFileDescriptor Descriptor)
        {
            PFD = Descriptor;
            IPackedFile PF = NBPackage.Read(PFD);
            Data = PF.UncompressedData;

            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);
            uint uBlockID = BR.ReadUInt32();
            int iIndex = 4;
            Debug.Assert(uBlockID == 0x46414D49);

            int iBlockVersion = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert((iBlockVersion == 78)      // Base game
                      || (iBlockVersion == 79)      // University
                      || (iBlockVersion == 80)      // Open for Business: Bluewater Village
                      || (iBlockVersion == 81)      // Open for Business
                      || (iBlockVersion == 84)      // Pets
                      || (iBlockVersion == 85));    // Bon Voyage
            // ToDo: Determine whether other versions are known and handled correctly

            int iBlockUnknown = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert(iBlockUnknown == 0);

            uLotInstance = BR.ReadUInt32();
            iIndex += 4;

            uint uPreviousLot = uLotInstance;
            if (iBlockVersion > 79)                     // Open for Business or higher
            {
                uPreviousLot = BR.ReadUInt32();
                iIndex += 4;
            }

            uint uVacationLot = 0;
            if (iBlockVersion > 84)                     // Bon Voyage or higher
            {
                uVacationLot = BR.ReadUInt32();
                iIndex += 4;
            }

            int iCreationOrder = BR.ReadInt32();
            iIndex += 4;

            int iMoney = BR.ReadInt32();
            iIndex += 4;

            int iFriends = BR.ReadInt32();
            iIndex += 4;

            uint uFlags = BR.ReadUInt32();
            // 0x01 = Has Phone
            // 0x02 = Has Baby
            // 0x04 = New Lot ?
            // 0x08 = Has Computer
            Debug.Assert(0 == (0xFFFFFFF0 & uFlags));
            iIndex += 4;

            int iMembers = BR.ReadInt32();
            iIndex += 4;
            if (0 == iMembers)
                uLotInstance = 0;

            for (int i = 0; i < iMembers; i++)
            {
                uint uSimID = BR.ReadUInt32();
                iIndex += 4;
                Debug.Assert((0 == uLotInstance) || (0 != uSimID));
            }

            uint uAlbumID = BR.ReadUInt32();
            iIndex += 4;

            int iHoodID = 0;
            if (iBlockVersion > 78)                     // University or higher
            {
                iHoodID = BR.ReadInt32();
                iIndex += 4;
            }

            int iBusinessMoney = 0;
            if (iBlockVersion > 80)                     // Open for Business or higher
            {
                iBusinessMoney = BR.ReadInt32();
                iIndex += 4;
            }

            if (0 != uLotInstance)
            {
                Debug.Assert(0 != uPreviousLot);
                Debug.Assert(0 < iCreationOrder);
                Debug.Assert(0 < iMoney);
                if (iBlockVersion > 78)                 // University or higher
                    Debug.Assert(0 < iHoodID);
                if (iBlockVersion > 80)                 // Open for Business or higher
                    Debug.Assert(0 < iBusinessMoney);
            }

            Debug.Assert(Data.Length == iIndex);
        }

        public uint LotInstance
        {
            get
            {
                return uLotInstance;
            }
        }
    }
}
