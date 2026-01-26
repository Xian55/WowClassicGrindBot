/*
  This file is part of ppather.

    PPather is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    PPather is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with ppather.  If not, see <http://www.gnu.org/licenses/>.

    Copyright Pontus Borg 2008

 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wmo;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct DoodadSet
{
    public readonly DoodadSetName name;
    public readonly uint firstInstance;
    public readonly uint nInstances;
    public readonly DoodadPadding padding;
}

[InlineArray(20)]
public struct DoodadSetName
{
    private char _element0;
}

[InlineArray(4)]
public struct DoodadPadding
{
    private char _element0;
}