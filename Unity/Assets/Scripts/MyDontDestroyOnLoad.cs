using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class MyDontDestroyOnLoad : MonoBehaviour {    
    public IntPtr windowPtr;
    public TestData testData;
    public int methodId;
    public List<int> methodPermutation;
    public bool ifInit;

    private void Awake()
    {
        methodId = 0;
        ifInit = false;
        InitMethodPermutation();
    }
    // Use this for initialization
    void Start () {		
	}
	
	// Update is called once per frame
	void Update () {
		
	}
    public void InitTestDataList() {        
        testData = new TestData();
    }
    public void RestoreData(Manager manager) {
        var td = testData;
        td.RestoreData(manager);
        StartCoroutine(InitMethodByProgress());        
    }

    public void InitMethodPermutation() {
        methodPermutation = new List<int>();        
        methodPermutation.Add(0);
        methodPermutation.Add(1);

        var result = "new permutation: ";
        for (int i = 0; i < methodPermutation.Count; i++) {            
            result += methodPermutation[i]+" ";
        }
        Debug.Log(result);
    }
    public int GetCurrentMethodTypeId() {
        if (methodPermutation == null)
            return -1;
        return methodPermutation[methodId];
    }
    public string GetMethodNameById(int id) {
        var re = "";
        switch (id)
        {
            case 0:
                re = "Single+Multi WithWindow";
                break;
            case 1:
                re = "Single+Multi+Pip WithWindow";
                break;
            default:
                re = "";
                break;
        }
        return re;

    }
    public string GetCurrentMethodName() {
        int id = GetCurrentMethodTypeId();
        return GetMethodNameById(id);
    }

    private IEnumerator InitMethodByProgress() {
        var timeStart = DateTime.Now.Ticks;
        var manager = GameObject.Find("Manager").GetComponent<Manager>();
        while (!manager.videoPlayer.isPrepared)
            yield return null;
        manager.videoPlayer.Pause();
        manager.mainCamera.transform.forward = Vector3.forward;
        switch (GetCurrentMethodTypeId()) {
            case 0:
                manager.panoramaVideoController.InitKoreaPlusMultiMethod();//single+multi
                break;
            case 1:
                manager.panoramaVideoController.InitKoreaPlusMultiPlusPipMethod();//single+multi+pip
                break;
        }
        Debug.Log("InitMethodByProgress successfully!");

        manager.panoramaVideoController.panelLoadingCover.SetActive(false);
        manager.panoramaVideoController.panelVideoEndCover.SetActive(false);
        manager.videoPlayer.frame = 0;
        VideoPlayOrReplay();        
        manager.panoramaVideoController.InitRecordParams();
        manager.panoramaVideoController.ChangeUIHideStatus();
        manager.panelVideoController.ButtonVoiceOnClick();
        manager.panelVideoController.UpdatePauseContinue(true);

        var timeEnd = DateTime.Now.Ticks;
        Debug.Log("InitMethodByProgress timeCost: " + (timeEnd - timeStart) / 1e7);
    }

    public void PrepareForCurrentRoundTest() {
        if (IfHasBottomSubWindow())
            SceneManager.LoadScene("SubWindowScene");
        else 
            SceneManager.LoadScene("NoSubWindowScene");
    }
    public bool IfHasBottomSubWindow() {
        var realMethodId = GetCurrentMethodTypeId();
        return realMethodId==0;
    }



    public bool IfVideoIsPlaying() {
        var manager = GameObject.Find("Manager").GetComponent<Manager>();
        return manager.videoPlayer.isPlaying;
    }
    public void ResetToTestStart() {
        ifInit = true;                
    }
    public void ResetMainCamera() {
        var manager = GameObject.Find("Manager").GetComponent<Manager>();
        manager.mainCamera.transform.forward = Vector3.forward;
        manager.panoramaVideoController.mainCamera.transform.forward = Vector3.forward;
    }
    public void VideoPlayOrReplay() {
        ResetMainCamera();
        var manager = GameObject.Find("Manager").GetComponent<Manager>();
        var multiMethod = manager.panoramaVideoController.multiMethod;
        var koreaMethod = manager.panoramaVideoController.koreaMethod;
        if (multiMethod != null && multiMethod.isReady) {
            multiMethod.ReplayInit();
        }
        if (koreaMethod != null && koreaMethod.isReady) {
            koreaMethod.InitKoreaMethodPaths();
        }

    }
}
