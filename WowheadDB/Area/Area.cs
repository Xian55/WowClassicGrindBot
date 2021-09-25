using System;
using System.Collections.Generic;
using System.Text;

namespace WowheadDB
{
    public class Area
    {
        public List<NPC> flightmaster;
        public List<NPC> innkeeper;
        public List<NPC> repair;
        public List<NPC> vendor;
        public List<NPC> trainer;

        public List<int> skinnable;
    }
}