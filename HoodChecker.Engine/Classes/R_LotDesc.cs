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
using System.Diagnostics;
using System.IO;
using SimPe.Interfaces.Files;

namespace LotExpander
{
    // Base class for TS2 lot description in neighborhood or lot package.
    public class R_LotDescription
    {
        public R_LotDescription()
        {
        }

        public virtual uint Instance
        {
            get
            {
                return 0;
            }
        }

        public virtual int Width
        {
            get
            {
                return 0;
            }
            set
            {
            }
        }

        public virtual int Height
        {
            get
            {
                return 0;
            }
            set
            {
            }
        }


        public virtual byte LotType
        {
            get
            {
                return 0;
            }
        }

        public virtual bool Occupied
        {
            get
            {
                return false;
            }
        }

        public virtual byte U10
        {
            get
            {
                return 0;
            }
            set
            {
            }
        }

        public virtual byte U11
        {
            get
            {
                return 0;
            }
        }

        public virtual string LotName
        {
            get
            {
                return "";
            }
            set
            {
            }
        }

        public virtual string LotDesc
        {
            get
            {
                return "";
            }
            set
            {
            }
        }
    }
}