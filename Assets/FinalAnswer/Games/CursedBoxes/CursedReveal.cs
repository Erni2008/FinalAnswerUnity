using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace FinalAnswer
{
    public static class CursedReveal
    {
        public static JToken Occupant(JObject panel,int box,long now)
        {
            // hostContents is deliberately unused: it contains unrevealed hiding assignments.
            var destroyed=panel.Arr("boxes").FirstOrDefault(b=>b.L("id")==box&&b.S("state")=="DESTROYED"&&b.S("nickname")!="");
            if(destroyed!=null)return destroyed;
            long start=panel.L("revealStartedAt");if(start==0||now<start)return null;
            var timing=panel.Obj("revealTimings");int index=0;
            double period=timing.D("panel")+timing.D("character")+timing.D("idle")+timing.D("gap");
            foreach(var survivor in panel.Arr("survivors").OrderBy(b=>b.L("boxId")))
            {
                long visibleAt=start+(long)Math.Round((index++*period+timing.D("anticipation")+timing.D("smoke"))*1000,MidpointRounding.AwayFromZero);
                if(survivor.L("boxId")==box)return now>=visibleAt?survivor:null;
            }
            return null;
        }
    }
}
