using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PipController : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    private Manager manager;
    public int id;
	// Use this for initialization
	void Start () {
        manager = GameObject.Find("Manager").GetComponent<Manager>();

    }
	
	// Update is called once per frame
	void Update () {
		
	}

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log(string.Format("pip{0} OnPointerClick", id));        
        if (manager.panoramaVideoController!=null)
            manager.panoramaVideoController.PanelNFOVOnClick(id);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log(string.Format("pip{0} OnPointerDown",id));
        manager.panoramaVideoController.OnPointerDown(eventData);
    }
}
