using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MenuBarButtonController : MonoBehaviour {
    private Manager manager;
    private MenuBarController menuBarController;

    public GameObject panel;
    void Awake()
    {
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        menuBarController = manager.menuBar.GetComponent<MenuBarController>();
    }

    // Update is called once per frame
    void Update()
    {

    }
    public void OnPointerEnter()
    {
        if (menuBarController.IfMenuBarButtonSelected())
        {
            menuBarController.eventSystem.SetSelectedGameObject(gameObject);
            panel.SetActive(true);
        }
    }
}
