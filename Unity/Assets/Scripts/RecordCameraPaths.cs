using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class RecordCameraPaths{
    public class MyVector2 {
        public float x, y;
        public MyVector2(float x, float y) {
            this.x = x;
            this.y = y;
        }
    }
    public List<MyVector2>[] initialPath;//初始路径
    public List<MyVector2>[] fovAwarePath;//fov感知路径
    public List<MyVector2>[] smoothPath;//平滑后的路径

    public static RecordCameraPaths CreateFromJSON(string jsonString)
    {
        //return JsonUtility.FromJson<RecordCameraPaths>(jsonString);
        return (RecordCameraPaths)JsonConvert.DeserializeObject(jsonString, typeof(RecordCameraPaths));
    }
    public string SaveToString()
    {
        //return JsonUtility.ToJson(this);
        return JsonConvert.SerializeObject(this);
    }

    public static Vector2 MyVector2ToVector2(MyVector2 v) {
        return new Vector2(v.x,v.y);
    }
    public static List<Vector2> MyVector2ToVector2(List<MyVector2> v)
    {
        var re = new List<Vector2>();
        foreach (var item in v) {
            re.Add(MyVector2ToVector2(item));
        }
        return re;
    }
    public static List<Vector2>[] MyVector2ToVector2(List<MyVector2>[] v)
    {
        if (v == null)
            return null;
        var re = new List<Vector2>[v.Length];
        for (int i = 0; i < v.Length; i++) {
            re[i] = MyVector2ToVector2(v[i]);
        }
        return re;
    }
    public static MyVector2 Vector2ToMyVector2(Vector2 v) {
        return new MyVector2(v.x, v.y);
    }

    public static List<MyVector2> Vector2ToMyVector2(List<Vector2> v) {
        var re = new List<MyVector2>();
        foreach (var item in v) {
            re.Add(Vector2ToMyVector2(item));
        }
        return re;
    }
    public static List<MyVector2>[] Vector2ToMyVector2(List<Vector2>[] v)
    {
        if (v == null)
            return null;
        var re = new List<MyVector2>[v.Length];
        for (int i = 0; i < v.Length; i++) {
            re[i] = Vector2ToMyVector2(v[i]);
        }
        return re;
    }
    public void SetInitialPath(List<Vector2>[] initialPath) {
        this.initialPath = Vector2ToMyVector2(initialPath);
    }
    public void SetFovAwarePath(List<Vector2>[] fovAwarePath)
    {
        this.fovAwarePath = Vector2ToMyVector2(fovAwarePath);
    }
    public void SetSmoothPath(List<Vector2>[] smoothPath)
    {
        this.smoothPath = Vector2ToMyVector2(smoothPath);
    }
    public void SetPaths(List<Vector2>[] initialPath, List<Vector2>[] fovAwarePath, List<Vector2>[] smoothPath) {
        SetInitialPath(initialPath);
        SetFovAwarePath(fovAwarePath);
        SetSmoothPath(smoothPath);
    }
    public List<Vector2>[] GetInitialPath()
    {
        return MyVector2ToVector2(initialPath);
    }
    public List<Vector2>[] GetFovAwarePath()
    {
        return MyVector2ToVector2(fovAwarePath);
    }
    public List<Vector2>[] GetSmoothPath()
    {
        return MyVector2ToVector2(smoothPath);
    }
}
