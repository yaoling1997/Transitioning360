using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class RecordRectOfPixel{
    public int downSampleWidth;
    public int downSampleHeight;
    public MyRect[,] rectOfPixel;
    public class MyRect
    {
        public float x, y, w, h;
        public MyRect(float x, float y,float w,float h)
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }
    }
    public static RecordRectOfPixel CreateFromJSON(string jsonString)
    {
        //return JsonUtility.FromJson<RecordCameraPaths>(jsonString);
        return (RecordRectOfPixel)JsonConvert.DeserializeObject(jsonString, typeof(RecordRectOfPixel));
    }
    public string SaveToString()
    {
        //return JsonUtility.ToJson(this);
        return JsonConvert.SerializeObject(this);
    }

    public static Rect MyRectToRect(MyRect r) {
        return new Rect(r.x,r.y,r.w,r.h);
    }
    public static Rect[,] MyRectToRect(MyRect[,] r)
    {
        if (r == null)
            return null;
        var w = r.GetLength(0);
        var h = r.GetLength(1);
        var re = new Rect[w, h];
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++) {
                var t = r[i, j];
                re[i, j] = new Rect(t.x,t.y,t.w,t.h);
            }                
        return re;
    }
    public static MyRect RectToMyRect(Rect r)
    {
        return new MyRect(r.x, r.y, r.width, r.height);
    }
    public static MyRect[,] RectToMyRect(Rect[,] r)
    {
        if (r == null)
            return null;
        var w = r.GetLength(0);
        var h = r.GetLength(1);
        var re = new MyRect[w, h];
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                var t = r[i, j];
                re[i, j] = new MyRect(t.x, t.y, t.width, t.height);
            }
        return re;
    }

    public Rect[,] GetRectOfPixel()
    {
        return MyRectToRect(rectOfPixel);
    }
    public void Set(int downSampleWidth,int downSampleHeight,Rect[,] rectOfPixel) {
        this.downSampleWidth = downSampleWidth;
        this.downSampleHeight = downSampleHeight;
        this.rectOfPixel = RectToMyRect(rectOfPixel);
    }
}
