using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AgeDropdownController : MonoBehaviour {
    private Dropdown dropdown;
    private void Awake()
    {
        dropdown = GetComponent<Dropdown>();
        var tmpList = new List<string>();
        for (int i = 15; i <= 60; i++)
            tmpList.Add(i.ToString());
        dropdown.AddOptions(tmpList);
    }
    // Use this for initialization
    void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}
