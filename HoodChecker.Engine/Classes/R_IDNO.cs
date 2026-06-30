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
using System.Diagnostics;
using System.IO;
using SimPe.Interfaces.Files;

namespace LotExpander
{
    public class R_IDNO
    {
        private bool Test_PrintDebugInfo = false;   // Enable (T) or disable (F) printing of debug information

        private byte[] Data;
        private IPackedFileDescriptor PFD;
        private uint uHoodID = 0;
        private uint uHoodType = 1;                 // Main hood

        public R_IDNO(IPackageFile NBPackage, IPackedFileDescriptor Descriptor)
        {
            PFD = Descriptor;
            IPackedFile PF = NBPackage.Read(PFD);
            Data = PF.UncompressedData;
            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);

            uint uVersion = BR.ReadUInt32();
            Debug.Assert((3 == uVersion)            // TS2
                      || (5 == uVersion)            // University
                      || (7 == uVersion)            // Nightlife
                      || (8 == uVersion)            // Open for Business
                      || (9 == uVersion)            // Pets
                      || (10 == uVersion)           // Seasons and above
            );   // ToDo: Determine whether other versions are known and handled correctly

            int iHoodPrefixLen = BR.ReadInt32();
            string sHoodPrefix = SimPe.Helper.ToString(BR.ReadBytes(iHoodPrefixLen));
            if (Test_PrintDebugInfo)
                Debug.Print("Hood Prefix: {0}", sHoodPrefix);

            uHoodID = BR.ReadUInt32();

            if (4 < uVersion)
                uHoodType = BR.ReadUInt32();
            Debug.Assert((1 == uHoodType)           // Primary hood
                      || (2 == uHoodType)           // Campus (University)
                      || (3 == uHoodType)           // Downtown (Nightlife)
                      || (4 == uHoodType)           // Suburb (Open for Business)
                      || (5 == uHoodType)           // Village (Bon Voyage)
                      || (6 == uHoodType)           // Lakes (Bon Voyage)
                      || (7 == uHoodType)           // Island (Bon Voyage)
            );   // ToDo: Determine whether other types are known and handled correctly
        }

        public uint HoodID
        {
            get
            {
                return uHoodID;
            }
        }

        public uint HoodType
        {
            get
            {
                return uHoodType;
            }
        }
    }
}
