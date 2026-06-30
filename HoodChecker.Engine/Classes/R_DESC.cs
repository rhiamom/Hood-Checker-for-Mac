/***************************************************************************
 *   Copyright (C) 2006 by Andi8104                                        *
 *   Andi8104@arcor.de                                                     *
 *                                                                         *
 *   Additional programming:                                               *
 *   Copyright (C) 2007-2011 by Mootilda                                   *
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
    public class R_DESC : R_LotDescription
    {
        private bool Test_PrintDebugInfo = false;   // Enable (T) or disable (F) printing of debug information

        private byte[] Data;
        private IPackedFileDescriptor PFD;
        private ushort uVersion1 = 0;
        private ushort uVersion2 = 0;
        private int iWidthIndex = 4;
        private int iWidth = 0;
        private int iHeightIndex = 8;
        private int iHeight = 0;
        private byte bType = 0xFF;
        private int iU10Index = 13;
        private byte bU10 = 0xFF;
        private byte bU11 = 0xFF;
        private int iLotNameLen = 0;
        private string sLotName = null;
        private int iDescIndex;
        private int iLotDescLen = 0;
        private string sLotDesc = null;
        private const int iMaxGrid = 128;
        private int iTerrainIndex;
        private float[] fTerrain = null;
        private int iTopIndex = 0;
        private int iTop = 0;
        private int iLeftIndex = 0;
        private int iLeft = 0;
        private int iElevIndex = 0;
        private float fElevation = 0;
        private int iLotNumberIndex = 0;
        private int iLotNumber = 0;
        private int iExtraIndex = 0;
        private float fExtra = 0;   // Another copy of the elevation?
        private int iLotClassValueIndex = 0;
        private uint iLotClassValue = 0;
        private int iClassOverrideIndex = 0;
        private byte bClassOverride = 0;
        private byte bOrientation = 0xFF;
        private int iSublotCount = 0;
        private string sFamilyName = null;
        private uint uHoodID = 0;

        public R_DESC(IPackageFile NBPackage, IPackedFileDescriptor LotDescriptor)
        {
            PFD = LotDescriptor;
            IPackedFile PF = NBPackage.Read(PFD);
            Data = PF.UncompressedData;
            BinaryReader BR = SimPe.Helper.GetBinaryReader(Data);
            int iIndex = 0;

            uVersion1 = BR.ReadUInt16();
            Debug.Assert( (uVersion1 == 13)
                       || (uVersion1 == 14)                         // Open for Business
                       || (uVersion1 == 18)                         // Apartment Life
            );   // ToDo: Determine whether other versions are known and handled correctly
            iIndex += 2;

            uVersion2 = BR.ReadUInt16();
            Debug.Assert((uVersion2 == 6)
                      || (uVersion2 == 7)                           // Bon Voyage
                      || (uVersion2 == 8)                           // Free Time
                      || (uVersion2 == 11)                          // Apartment Life
            );   // ToDo: Determine whether other versions are known and handled correctly
            iIndex += 2;

            Debug.Assert(iWidthIndex == iIndex);
            iWidthIndex = iIndex;
            iWidth = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert((0 < Width) && (Width <= 6));

            Debug.Assert(iHeightIndex == iIndex);
            iHeightIndex = iIndex;
            iHeight = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert((0 < Height) && (Height <= 6));

            bType = BR.ReadByte();
            Debug.Assert((bType == 0)   // Residential
                      || (bType == 1)   // Community
                      || (bType == 2)   // University: Dorm
                      || (bType == 3)   // University: Greek House
                      || (bType == 4)   // University: Secret Society?
                      || (bType == 5)   // Bon Voyage: Hotel
                      || (bType == 6)   // Bon Voyage: Hidden Vacation Lot
                      || (bType == 7)   // FreeTime: Hidden Hobby Lot
                      || (bType == 8)   // Apartment Life: Apartment Base
                      || (bType == 9)   // Apartment Life: Apartment Sublot
                      || (bType == 10)  // Apartment Life: Hidden Lot (Witches)
            );
            iIndex++;

            Debug.Assert(iU10Index == iIndex);
            iU10Index = iIndex;
            bU10 = BR.ReadByte();
            Debug.Assert(bU10 < 0x10);
            iIndex++;

            bU11 = BR.ReadByte();
            Debug.Assert(bU11 < 4);
            iIndex++;

            int iDummy = BR.ReadInt32();
            iIndex += 4;

            iLotNameLen = BR.ReadInt32();
            sLotName = SimPe.Helper.ToString(BR.ReadBytes(iLotNameLen));
            iIndex += 4 + iLotNameLen;
            if (Test_PrintDebugInfo)
                Debug.Print("Lot Name: {0}", sLotName);

            iDescIndex = iIndex;
            iLotDescLen = BR.ReadInt32();
            sLotDesc = SimPe.Helper.ToString(BR.ReadBytes(iLotDescLen));
            iIndex += 4 + iLotDescLen;
            if (Test_PrintDebugInfo)
                Debug.Print("Lot Desc: {0}", sLotDesc);

            iTerrainIndex = iIndex;
            int iArrayLen = BR.ReadInt32();
            fTerrain = new float[iArrayLen];
            // Is this the relative elevations from NHTG for this lot?
            // Debug.Assert(iArrayLen == ((iHeight + 1) * (iWidth + 1)));
            for (int i = 0; i < iArrayLen; i++)
            {
                fTerrain[i] = BR.ReadSingle();
                // Debug.Assert(0 == fTerrain[i]);
            }
            iIndex += 4 + iArrayLen * 4;

            if ((uVersion2 >= 7))
            {
                iExtraIndex = iIndex;
                fExtra = BR.ReadSingle();
                iIndex += 4;
                if (uVersion2 >= 8)
                {
                    iDummy = BR.ReadInt32();
                    iIndex += 4;
                    if (uVersion2 == 11)    // Apartment Life
                    {
                        byte bNumberOfApts = BR.ReadByte();
                        iIndex++;

                        int iPrice1 = BR.ReadInt32();
                        iIndex += 4;
                        int iPrice2 = BR.ReadInt32();
                        iIndex += 4;

                        // Note: may be unsigned value, instead of signed.
                        iLotClassValueIndex = iIndex;
                        iLotClassValue = BR.ReadUInt32();
                        iIndex += 4;

                        iClassOverrideIndex = iIndex;
                        bClassOverride = BR.ReadByte();
                        Debug.Assert((0 == bClassOverride) || (1 == bClassOverride));
                        iIndex++;

                        if (bNumberOfApts > 0)
                        {
                            Debug.Assert(iPrice2 <= iPrice1);
                            if (Test_PrintDebugInfo)   // if (0 != bClassOverride)
                                Debug.Print("Lot{0} \"{5}\" Apartment price range: {1} - {2} Class {3}({4})", PFD.Instance, iPrice2, iPrice1, bClassOverride, iLotClassValue, sLotName);
                        }
                        else
                        {
                            Debug.Assert((-1 == iPrice1) || (0 == iPrice1));
                            Debug.Assert((0 == iPrice2) || (32000 == iPrice2));
                            if (Test_PrintDebugInfo)   // if (0 != bClassOverride)
                                Debug.Print("Lot{0} \"{5}\" Price range: {1} - {2} Class {3}({4})", PFD.Instance, iPrice2, iPrice1, bClassOverride, iLotClassValue, sLotName);
                        }
                    }
                }
            }

            iTopIndex = iIndex;
            iTop = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert((0 <= iTop) && (iTop < iMaxGrid));

            iLeftIndex = iIndex;
            iLeft = BR.ReadInt32();
            iIndex += 4;
            Debug.Assert((0 <= iLeft) && (iLeft < iMaxGrid));

            iElevIndex = iIndex;
            fElevation = BR.ReadSingle();
            iIndex += 4;

            iLotNumberIndex = iIndex;
            iLotNumber = BR.ReadInt32();
            Debug.Assert(iLotNumber == PFD.Instance);
            iIndex += 4;

            bOrientation = BR.ReadByte();
            Debug.Assert(bOrientation < 4);
            iIndex += 1;

            int iTextureLen = BR.ReadInt32();
            string sTexture = SimPe.Helper.ToString(BR.ReadBytes(iTextureLen));
            iIndex += 4 + sTexture.Length;

            byte b = BR.ReadByte();
            iIndex++;
            // Debug.Assert(0 == b);

            if (14 <= uVersion1)    // Open for Business
            {
                uint uOwner = BR.ReadUInt32();  // Sim info instance number
                iIndex += 4;

                if (Test_PrintDebugInfo && (0 != uOwner))
                    Debug.Print("Business owned by 0x{0:X8}", uOwner);
            }
            if (11 == uVersion2)    // Apartment Life
            {
                if (bType == 8)
                {
                    if (Test_PrintDebugInfo)
                        Debug.Print("Lot{0} Apartment Base: {1}", PFD.Instance, LotName);
                }
                else if (bType == 9)
                {
                    if (Test_PrintDebugInfo)
                        Debug.Print("Lot{0} Apartment Sublot: {1}", PFD.Instance, LotName);
                }

                uint uAptBase = BR.ReadUInt32();
                iIndex += 4;
                // Only an apartment sublot should have an associated apartment base:
                if (bType == 9)
                {
                    Debug.Assert(uAptBase != 0);
                    if (Test_PrintDebugInfo)
                        Debug.Print("Base lot: {0}", uAptBase);
                }
                else
                    Debug.Assert(uAptBase == 0);

                for (int i = 0; i < 9; i++)
                {
                    b = BR.ReadByte();
                    iIndex++;
                    Debug.Assert(0 == b);
                }

                iSublotCount = BR.ReadInt32();
                iIndex += 4;
                Debug.Assert(iSublotCount < 5);
                for (int i = 0; i < iSublotCount; i++)
                {
                    uint uAptSublot = BR.ReadUInt32();
                    iIndex += 4;

                    uint uFamily = BR.ReadUInt32();     // Family info instance number
                    iIndex += 4;

                    uint u2 = BR.ReadUInt32();
                    iIndex += 4;

                    uint uRoommate = BR.ReadUInt32();   // Sim Description instance number
                    iIndex += 4;

                    if (Test_PrintDebugInfo)
                        Debug.Print("Occupied lot: Sublot={0} Family={1} {2} Roommate={3}", uAptSublot, uFamily, u2, uRoommate);
                }

#if DEBUG
                int iCount = BR.ReadInt32();
                iIndex += 4;
                for (int i = 0; i < iCount; i++)
                {
                    uint uDummy = BR.ReadUInt32();
                    iIndex += 4;
                    // Debug.Assert(0 == uDummy);
                }
                Debug.Assert(Data.Length == iIndex);
#endif
            }

            Debug.Assert(Data.Length == iIndex);
        }

        public override uint Instance
        {
            get
            {
                return PFD.Instance;
            }
        }

        private void ReplaceUInt(uint iOld, uint iNew, int iIndex)
        {
            byte[] BA = new byte[4];

            Array.Copy(Data, iIndex, BA, 0, 4);
            BinaryReader BR = SimPe.Helper.GetBinaryReader(BA);
            uint iInt = BR.ReadUInt32();
            Debug.Assert(iInt == iOld);

            BinaryWriter BW = new BinaryWriter(new MemoryStream(BA));
            BW.Write(iNew);
            Array.Copy(BA, 0, Data, iIndex, 4);
            PFD.SetUserData(Data, true);
        }

        private void ReplaceInt(int iOld, int iNew, int iIndex)
        {
            byte[] BA = new byte[4];

            Array.Copy(Data, iIndex, BA, 0, 4);
            BinaryReader BR = SimPe.Helper.GetBinaryReader(BA);
            int iInt = BR.ReadInt32();
            Debug.Assert(iInt == iOld);

            BinaryWriter BW = new BinaryWriter(new MemoryStream(BA));
            BW.Write(iNew);
            Array.Copy(BA, 0, Data, iIndex, 4);
            PFD.SetUserData(Data, true);
        }

        public override int Width
        {
            get
            {
                return iWidth;
            }
            set
            {
                if (iWidth != value)
                {
                    ReplaceInt(iWidth, value, iWidthIndex);
                    iWidth = value;
                }
            }
        }

        public override int Height
        {
            get
            {
                return iHeight;
            }
            set
            {
                if (iHeight != value)
                {
                    ReplaceInt(iHeight, value, iHeightIndex);
                    iHeight = value;
                }
            }
        }

        private void ReplaceFloat(float fOld, float fNew, int iIndex)
        {
            byte[] BA = new byte[4];

            Array.Copy(Data, iIndex, BA, 0, 4);
            BinaryReader BR = SimPe.Helper.GetBinaryReader(BA);
            float fFloat = BR.ReadSingle();
            Debug.Assert(fFloat == fOld);

            BinaryWriter BW = new BinaryWriter(new MemoryStream(BA));
            BW.Write(fNew);
            Array.Copy(BA, 0, Data, iIndex, 4);
            PFD.SetUserData(Data, true);
        }

        public float Elevation
        {
            get
            {
                return fElevation;
            }
            set
            {
                if (fElevation != value)
                {
                    if (fElevation == fExtra)
                    {
                        ReplaceFloat(fExtra, value, iExtraIndex);
                        fExtra = value;
                    }
                    ReplaceFloat(fElevation, value, iElevIndex);
                    fElevation = value;
                }
            }
        }

        public override byte LotType
        {
            get
            {
                return bType;
            }
        }

        public override bool Occupied
        {
            get
            {
                return (0 != iSublotCount);
            }
        }

        public bool HasClassValue
        {
            get
            {
                return (11 == uVersion2);
            }
        }

        public uint LotClassValue
        {
            get
            {
                // If Apartment Life, this should be set, otherwise 0
                return (iLotClassValue);
            }
            set
            {
                // If not Apartment Life, this should never be called:
                Debug.Assert(0 != iClassOverrideIndex);
                Debug.Assert(0 != iLotClassValueIndex);

                // Only called to override value
                Debug.Assert(bClassOverride == Data[iClassOverrideIndex]);
                Data[iClassOverrideIndex] = bClassOverride = 1;
                ReplaceUInt(iLotClassValue, value, iLotClassValueIndex);
            }
        }

        public int LotClassValueOverride
        {
            get
            {
                // If Apartment Life, this should be 0 or 1, otherwise 0
                return (bClassOverride);
            }
        }

        public void ClearLotClassValue(uint value)
        {
            // If not Apartment Life, this should never be called:
            Debug.Assert(0 != iClassOverrideIndex);
            Debug.Assert(0 != iLotClassValueIndex);

            // Only called to clear value
            Debug.Assert(bClassOverride == Data[iClassOverrideIndex]);
            Data[iClassOverrideIndex] = bClassOverride = 0;
            ReplaceUInt(iLotClassValue, value, iLotClassValueIndex);
        }

        public override byte U10
        {
            get
            {
                return bU10;
            }
            set
            {
                Debug.Assert(bU10 == Data[iU10Index]);
                Data[iU10Index] = bU10 = value;
                PFD.SetUserData(Data, true);
            }
        }

        public override byte U11
        {
            get
            {
                return bU11;
            }
        }

        public override string LotName
        {
            get
            {
                return sLotName;
            }
            set
            {
                sLotName = value;
            }
        }

        public string FamilyName
        {
            set
            {
                sFamilyName = value;
            }
        }

        public override string ToString()
        {
            string s = sLotName;
            if (null != sFamilyName)
                s += " [" + sFamilyName + "]";
            return s;
        }

        public override string LotDesc
        {
            get
            {
                return sLotDesc;
            }
        }

        public int Top
        {
            get
            {
                return iTop;
            }
            set
            {
                ReplaceInt(iTop, value, iTopIndex);
                iTop = value;
            }
        }

        public int Left
        {
            get
            {
                return iLeft;
            }
            set
            {
                ReplaceInt(iLeft, value, iLeftIndex);
                iLeft = value;
            }
        }

        public byte Orientation
        {
            get
            {
                return bOrientation;
            }
        }

        public uint HoodID
        {
            get
            {
                return uHoodID;
            }
            set
            {
                uHoodID = value;
            }
        }
    }
}
