using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PanelNFOVController : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
{
    public int id;
    private Manager manager;
    private float textureWidth;
    private float textureHeight;

    private void Awake()
    {
        manager = GameObject.Find("Manager").GetComponent<Manager>();
    }
    // Use this for initialization
    void Start () {
    }
	
	// Update is called once per frame
	void Update () {        
        var t = GetComponent<RawImage>().texture;
        if (t != null)
        {
            textureWidth = t.width;
            textureHeight = t.height;
            //Debug.Log(string.Format("textureWidth,textureHeight: {0},{1}", textureWidth, textureHeight));
        }
        else {
            textureWidth = 1;
            textureHeight = 1;
        }
        var rt = GetComponent<RectTransform>();
        //var rect = rt.rect;
        //Debug.Log(rect);
        //Debug.Log(string.Format("rt.x,y: {0}, {1}",rt.sizeDelta.x, rt.sizeDelta.y));
        var x = rt.sizeDelta.y / textureHeight * textureWidth;
        rt.sizeDelta = new Vector2(x, rt.sizeDelta.y);
        //Debug.Log("width/height:" + rt.sizeDelta.x / rt.sizeDelta.y);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("panelNFOV OnPointerClick");
        if (manager.panoramaVideoController!=null)
            manager.panoramaVideoController.PanelNFOVOnClick(id);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log("panelNFOV OnPointerDown");        
    }
}
