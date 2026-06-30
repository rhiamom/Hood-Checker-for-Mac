/***************************************************************************
 *   Copyright (C) 2007-2009 by Mootilda                                   *
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
using SimPe.Interfaces.Files;

namespace LotExpander
{
    public class R_VERS
    {
        private IPackedFileDescriptor PFD;
        private const uint uUnknown = 0xFFFFFFFF; // Greater than any known version
        private string sVersionString = null;
        private string sVersionNumber = null;
        private uint uVersion = uUnknown;

        public R_VERS(IPackageFile LotPackage, IPackedFileDescriptor Descriptor, string[] saVersionStrings)
        {
            PFD = Descriptor;
            IPackedFile PF = LotPackage.Read(PFD);
            BinaryReader BR = SimPe.Helper.GetBinaryReader(PF.UncompressedData);
            BR.ReadUInt32(); // Type = CBE750E0
            BR.ReadInt16();  // Version
            int iNumberOfEntries = BR.ReadInt32();
            for (int i = 0; i < iNumberOfEntries; i++)
            {
                uint   uDataType  = BR.ReadUInt32();
                int    iFormatLen = BR.ReadInt32();
                string sFormat    = SimPe.Helper.ToString(BR.ReadBytes(iFormatLen));
                switch (uDataType)
                {
                    case 0x0B8BEA18: // (string) = length of data (4 bytes) + data 
                    {
                        int    iVersLen = BR.ReadInt32();
                        byte[] bVersion = BR.ReadBytes(iVersLen);
                        if (sFormat == "0")
                            sVersionString = SimPe.Helper.ToString(bVersion);
                        else if (sFormat == "1")
                            sVersionNumber = SimPe.Helper.ToString(bVersion);
                        break;
                    }
                    case 0x0C264712: // (int) = 4 bytes
                    case 0xABC78708: // (float) = 4 bytes 
                    case 0xEB61E4F7: // (int) = 4 bytes
                    {
                        BR.ReadUInt32();
                        break;
                    }
                    case 0xCBA908E1: // (boolean) = 1 byte 
                    {
                        BR.ReadBoolean();
                        break;
                    }
                    default:
                    {
                        // We don't know how to handle this entry, so we're better off stopping now.
                        i = iNumberOfEntries;
                        break;
                    }
                }
            }
            if (sVersionNumber != null)
            {
                // Format 1 has format "1.<expansion pack number>.<other version information>"
                char[] delimiter = new char[] {'.'};
                string[] sNumber = sVersionNumber.Split(delimiter);
                if (sNumber[0] == "1")
                {
                    try
                    {
                        uVersion = System.Convert.ToUInt32(sNumber[1]);
                    } 
                    catch
                    {
                        uVersion = uUnknown;
                    }
                }
            }
            if ((uVersion == uUnknown) && (sVersionString != null))
            {
                for (uint i = 0; i < saVersionStrings.Length; i++)
                {
                    if (sVersionString == saVersionStrings[i])
                    {
                        uVersion = i;
                    }
                }
            }
            if (sVersionString == null)
            {
                sVersionString = (uVersion == uUnknown) ? "Unknown Sims 2 Version": saVersionStrings[uVersion];
            }
        }

        public uint VersionNumber
        {
            get
            {
                return (uVersion);
            }
        }

        public string VersionString
        {
            get
            {
                return (sVersionString);
            }
        }
    }
}
