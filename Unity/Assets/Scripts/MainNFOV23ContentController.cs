using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainNFOV23ContentController : MonoBehaviour {

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        var prt = transform.parent.GetComponent<RectTransform>();
        var rt = transform.GetComponent<RectTransform>();
        var x = prt.sizeDelta.x;
        var y = x * 9 / 16;
        rt.sizeDelta = new Vector2(x, y);
	}
}
