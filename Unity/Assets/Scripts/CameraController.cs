using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour {
    private Camera mainCamera;
    public float speed;
    void Awake() {
        mainCamera = GetComponent<Camera>();
    }
	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        UpdateCamera();
    }
    private void UpdateCamera() {
        var rotateV = new Vector3(-Input.GetAxis("Vertical") * speed * Time.deltaTime, Input.GetAxis("Horizontal") * speed * Time.deltaTime,0);
        var newLocalEulerAngles = mainCamera.transform.localEulerAngles + rotateV;
        var x = newLocalEulerAngles.x;
        if (x > 180)//Turn to - 90 ~ 90, because the angle represents the range of 0 ~ 360
            x -= 360;
        newLocalEulerAngles.x = Mathf.Clamp(x, -90, 90);
        mainCamera.transform.localEulerAngles = newLocalEulerAngles;        
    }
}
