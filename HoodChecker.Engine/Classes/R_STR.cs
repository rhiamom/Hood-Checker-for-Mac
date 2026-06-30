/***************************************************************************
 *   Copyright (C) 2010-2011 by Mootilda                                   *
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
    public class R_STR
    {
        private bool Test_PrintDebugInfo = false;   // Enable (T) or disable (F) printing of debug information

        private IPackedFileDescriptor PFD;
        private byte[] Data;
        private const int iLeadingZeros = 64;

        private IDictionary<ushort, string> RefToString = new Dictionary<ushort, string>();

        public R_STR(IPackageFile NBPackage, IPackedFileDescriptor Descriptor)
        {
            PFD = Descriptor;
            IPackedFile PF = NBPackage.Read(PFD);
            Data = PF.UncompressedData;

            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);
            byte[] bDummy = BR.ReadBytes(iLeadingZeros);

            ushort uFormatCode = BR.ReadUInt16();
            if (uFormatCode != 0xfffd)
                throw new InvalidDataException("Invalid STR#: Format Code");

            ushort iCount = BR.ReadUInt16();
            for (ushort i = 0; i < iCount; ++i)
            {
                byte uLanguageCode = BR.ReadByte(); // language code
                string sValue = ReadNullTerminatedString(BR);
                string sDesc = ReadNullTerminatedString(BR);
                if (0 == sValue.Length)
                    continue;
                if (RefToString.Contains(new KeyValuePair<ushort, string>(i, sValue)))
                    continue;
                RefToString.Add(i, sValue);
            }
        }

        // Read in a null-terminated string
        private string ReadNullTerminatedString(BinaryReader BR)
        {
            string s = "";
            for (;;)
            {
                char c = BR.ReadChar();
                if (0 == c)
                    break;
                s += c;
            }
            return s;
        }

        public string FindString(ushort uRef)
        {
            // First, check whether the string is already in the dictionary
            ICollection<ushort> uReferences = RefToString.Keys;
            if (uReferences.Contains(uRef))
            {
                string sValue = RefToString[uRef];
                return sValue;
            }
            return null;
        }
    }
}
