using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonController : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler {
    private Color originColor;
    private Color changeColor;
    private Image image;
    // Use this for initialization
    void Start () {
        image = GetComponent<Image>();
        originColor = image.color;
        changeColor = new Color(0, 182f/255, 1);
    }

    // Update is called once per frame
    void Update () {
		
	}

    public void OnPointerEnter(PointerEventData eventData)
    {
        image.color = changeColor;        
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        image.color = originColor;        
    }
}
