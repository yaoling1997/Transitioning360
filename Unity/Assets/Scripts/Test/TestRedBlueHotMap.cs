using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TestRedBlueHotMap : MonoBehaviour {
    private Texture2D t;
    private List<Color> colorList;//渐变颜色列表
    public Slider slider;
    public RawImage colorBar;//颜色条
    private void Awake()
    {
        colorList = new List<Color>();
        colorList.Add(Color.blue);
        colorList.Add(Color.cyan);
        colorList.Add(Color.green);
        colorList.Add(new Color(1, 1, 0));
        colorList.Add(Color.red);
        var cbt = new Texture2D(20, 500);
        for (int i = 0; i < cbt.width; i++)
            for (int j = 0; j < cbt.height; j++) {
                var v = (float)j / cbt.height;
                var c = ValueToColor(v);
                cbt.SetPixel(i, j, c);
            }
        cbt.Apply();
        colorBar.texture = cbt;
    }
    // Use this for initialization
    void Start () {
        var rawImage = GetComponent<RawImage>();
        t = new Texture2D(100,100);
        rawImage.texture = t;

    }

    public Color ValueToColor(float v) {
        var c = Color.clear;
        if (v < 0 || v > 1)
        {
            Debug.LogError("v out of range!");
            return c;
        }
        int segNum = colorList.Count - 1;
        float vSeg = 1f / segNum;
        int colorId = Mathf.Min((int)(v / vSeg), segNum - 1);
        v -= colorId * vSeg;
        var c1 = colorList[colorId];
        var c2 = colorList[colorId + 1];
        var p1 = new Vector3(c1.r, c1.g, c1.b);
        var p2 = new Vector3(c2.r, c2.g, c2.b);
        var p = p1 + (p2 - p1) * v / vSeg;
        c = new Color(p.x, p.y, p.z);
        //float midV = 0.5f;
        //if (v < midV)
        //{
        //    c = (Color.blue * (midV - v) + Color.green * v) / midV;
        //}
        //else {
        //    v -= midV;
        //    c = (Color.green * (midV - v) + Color.red * v) / midV;
        //}
        return c;
    }
	// Update is called once per frame
	void Update () {
		
	}
    
    public void OnSliderValueChanged() {
        var newC = ValueToColor(slider.value);
        for (int i = 0; i < t.width; i++)
            for (int j = 0; j < t.height; j++)
                t.SetPixel(i, j, newC);
        t.Apply();
        Debug.Log("newC: "+newC);
    }
}
