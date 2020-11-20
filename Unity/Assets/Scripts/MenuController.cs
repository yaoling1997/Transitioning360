using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour {

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		
	}
    public void ButtonPlayVideo360OnClick() {
        SceneManager.LoadScene("PlayVideo360Scene");
    }
    public void ButtonAnotateVideoOnClick()
    {
        SceneManager.LoadScene("AnnotateVideoScene");
    }
}
