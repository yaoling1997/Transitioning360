using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

[System.Serializable]
public class InterestingPointRecord {
    public SaveVector2 pixelPos;
    public double t;//time of video
    public long systemTime;//The system time of this operation is counted from 1970 01 01:00
    public int opt;//Operation, 1 for add, - 1 for delete
    public InterestingPointRecord(Vector2 pixelPos, double t,int opt){
        this.pixelPos = new SaveVector2(pixelPos);
        this.t = t;
        systemTime = DateTime.Now.Ticks;
        this.opt = opt;
    }
    public class InterestingPointRecordComparer : IComparer<InterestingPointRecord>
    {
        public int Compare(InterestingPointRecord x, InterestingPointRecord y)
        {
            if (x.t == y.t)
            {
                return x.systemTime < y.systemTime ? -1 : 1;
            }                
            return x.t < y.t ? -1 : 1;
        }
    }

    [System.Serializable]
    public class SaveVector2 {
        public int x, y;
        public SaveVector2(float x, float y) {
            this.x = (int)x;
            this.y = (int)y;
        }
        public SaveVector2(Vector2 v) {
            x = (int)v.x;
            y = (int)v.y;
        }
        public Vector2 ToVector2() {
            return new Vector2(x,y);
        }        
    }
}
