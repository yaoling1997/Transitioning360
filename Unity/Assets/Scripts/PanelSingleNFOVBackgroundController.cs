using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PanelSingleNFOVBackgroundController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public void OnDrag(PointerEventData eventData)
    {
        Debug.Log("PanelSingleNFOVBackground OnDrag");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log("PanelSingleNFOVBackground OnPointerDown");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Debug.Log("PanelSingleNFOVBackground OnPointerUp");
    }

    // Use this for initialization
    void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        var prt = transform.parent.GetComponent<RectTransform>();
        var rt = transform.GetComponent<RectTransform>();
        var x = prt.sizeDelta.x;
        var y = x * 2.25f / 16;
        rt.sizeDelta = new Vector2(x, y);
	}
}
