using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Threading;
using UnityEngine.Video;
using System;
using System.IO;
using System.Runtime.InteropServices;

public class PanoramaVideoController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public readonly static KeyCode changeUIHideStatusKey = KeyCode.H;
    public readonly static KeyCode switchMultiFollowKey = KeyCode.F;//When the multi method is enabled, it is used to switch whether to follow or not    
    public Camera mainCamera;
    public float pixelPerDegree;    

    private Manager manager;
    private MenuBarController menubarController;
    private VideoPlayer videoPlayer;
    private Vector2 originPoint;    
    private Vector3 camEulerAngle;//pointerDown时的eulerAngle    
    private PanelVideoController panelVideoController;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    public KoreaMethod koreaMethod;
    public MultiMethod multiMethod;
    public PipMethod pipMethod;
    
    public GameObject panelVideoEndCover;//视频播放结束封面    
    public GameObject panelLoadingCover;//loading封面
    public GameObject panelTestStartCover;//测试开始的封面        

    private int display1Width = 1280;
    private int display1Height = 720;
    private int display2Width = 1280;
    private int display2Height = 900;

    public IntPtr windowPtr;

    public MyDontDestroyOnLoad dontDestroyOnLoad;

    //Record user feedback in the background
    public bool ifDragged;
    public int pointerDownFrame;//What frame is when the mouse is clicked in the video
    public long pointerDownRealTime;//Real time of mouse down
    public Vector2 prePointerDragPoint;//Point of previous mouse drag
    public float dragDistance;
    public float minDragDist;

    public int pipWindowRecordTime;//PIP window size record times
    public Vector2 sumPipWindowSize;//Cumulative PIP window size
    public Vector2 finalPipWindowSize;//Final PIP window size

    private Coroutine testC;

    private Dropdown userInterfaceDropdown;
    
    public void InitRecordParams() {
        ifDragged = false;
        pipWindowRecordTime = 0;
        sumPipWindowSize = Vector2.zero;
        finalPipWindowSize = Vector2.zero;
        minDragDist = 1;
    }
    
    public bool OnBorder(int x,int y,int w,int h,int borderW){
        return x < borderW || w - 1 - x < borderW || y < borderW || h - 1 - y < borderW;
    }

    void Awake() {
#if UNITY_EDITOR

#else        
        if (Manager.GetActivateSceneNumber() == 2)
        {
            Screen.SetResolution(display1Width, display1Height, false);
        }
        else if (Manager.GetActivateSceneNumber() == 3) {
            Screen.SetResolution(display2Width, display2Height, false);
        }        
#endif
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        videoPlayer = manager.videoPlayer;
        menubarController = manager.menuBar.GetComponent<MenuBarController>();
        panelVideoController = manager.panelVideoController;
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        
        var dontDestroyOnLoadGameObj = GameObject.Find("DontDestroyOnLoad");
        if (dontDestroyOnLoadGameObj != null)
            dontDestroyOnLoad = dontDestroyOnLoadGameObj.GetComponent<MyDontDestroyOnLoad>();

        var sceneNumber = Manager.GetActivateSceneNumber();
        if (sceneNumber >= 2)
            PrepareCovers();

        UpdateTextMethodStatus(0);        
        if (dontDestroyOnLoadGameObj == null)
        {
            dontDestroyOnLoadGameObj = new GameObject("DontDestroyOnLoad");
            dontDestroyOnLoad = dontDestroyOnLoadGameObj.AddComponent<MyDontDestroyOnLoad>();
            //Get the window handle.            
            windowPtr = FindWindow(null, Manager.productName);
            dontDestroyOnLoad.windowPtr = windowPtr;
            DontDestroyOnLoad(dontDestroyOnLoad);
            if (panelTestStartCover != null)
                panelTestStartCover.SetActive(true);
        }
        else {            
            windowPtr = dontDestroyOnLoad.windowPtr;            
            if (panelTestStartCover != null)
                panelTestStartCover.SetActive(false);
            dontDestroyOnLoad.RestoreData(manager);
        }
        manager.textMethodStatus.gameObject.SetActive(true);        
        minDragDist = 1;
    }
    
    private void PrepareCovers() {
        
        var canvas = GameObject.Find("Canvas");
        if (panelVideoEndCover == null) {
            panelVideoEndCover = Instantiate(manager.prefabPanelVideoEndCover, canvas.transform);
            var buttonReplay = panelVideoEndCover.transform.Find("ButtonReplay").GetComponent<Button>();                        
            buttonReplay.onClick.AddListener(ButtonReplayOnClick);            
        }
        
        if (panelLoadingCover == null)
        {
            panelLoadingCover = Instantiate(manager.prefabPanelLoadingCover, canvas.transform);            
        }
        
        if (panelTestStartCover == null)
        {
            panelTestStartCover = Instantiate(manager.prefabPanelTestStartCover, canvas.transform);
            var buttonLoadTestData = panelTestStartCover.transform.Find("ButtonLoadTestData").GetComponent<Button>();
            var buttonTestStart = panelTestStartCover.transform.Find("ButtonTestStart").GetComponent<Button>();
            userInterfaceDropdown = panelTestStartCover.transform.Find("DropdownInterfaces").GetComponent<Dropdown>();
            buttonLoadTestData.onClick.AddListener(ButtonLoadTestDataOnClick);
            buttonTestStart.onClick.AddListener(ButtonTestStartOnClick);
            userInterfaceDropdown.onValueChanged.AddListener(delegate {
                DropdownOnValueChanged();
            });
        }
    }
    // Use this for initialization
    void Start() {
        pixelPerDegree = 15;
    }

    // Update is called once per frame
    void Update() {

        if (Input.GetKeyDown(changeUIHideStatusKey))
        {
            ChangeUIHideStatus();
        }
        if (Input.GetKeyDown(switchMultiFollowKey)) {
            if (multiMethod != null && multiMethod.isReady) {
                multiMethod.SwitchFollowMode();
            }
        }        
        if (videoPlayer.isPlaying && IfUseKoreaPlusMultiPlusPipMethod()) {
            sumPipWindowSize += pipMethod.GetNewPipWindowSize();
            pipWindowRecordTime++;
        }
    }

    //Hiding UI that is not related to video content
    public void ChangeUIHideStatus() {
        manager.panelVideoControllerBackground.SetActive(!manager.panelVideoControllerBackground.activeSelf);
        manager.menuBar.SetActive(!manager.menuBar.activeSelf);
    }
    public long GetRealTime() {
        return DateTime.Now.Ticks;
    }
    public void OnPointerDown(PointerEventData data)
    {
        originPoint = data.position;
        camEulerAngle = mainCamera.transform.eulerAngles;
        ifDragged = false;
        dragDistance = 0;
        pointerDownFrame = (int)videoPlayer.frame;
        pointerDownRealTime = GetRealTime();
        prePointerDragPoint = originPoint;
        Debug.Log("canvas OnPointerDown");        
    }
    public void OnDrag(PointerEventData data)
    {
        ifDragged = true;
        Vector2 nowPoint = data.position;
        var oldAngle = panelVideoController.EulerAngleToAngle(camEulerAngle);
        var delta = (-nowPoint + originPoint) / pixelPerDegree;
        var newAngle = oldAngle + delta;
        newAngle = new Vector2(Mo(newAngle.x, 360), Mathf.Min(Mathf.Max(-90, newAngle.y), 90));
        mainCamera.transform.eulerAngles = panelVideoController.AngleToEulerAngle(newAngle);
        ChangedManually();
        var dist = (nowPoint - prePointerDragPoint).magnitude;
        dragDistance += dist;
        prePointerDragPoint = nowPoint;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (ifDragged && dragDistance> minDragDist) {
            Debug.Log("drag distance: "+ dragDistance);
            var endPoint = eventData.position;            
        }
        ifDragged = false;
        dragDistance = 0;
        Debug.Log("canvas OnPointerUp");
    }

    
    public float Mo(float a, float b) {
        return (a % b + b) % b;
    }

    public int GetCurrentKeyFrame()
    {
        if (opticalFlowCamerasController.pixelSaliency == null)
        {
            return 0;
        }
        int totalKeyFrame = opticalFlowCamerasController.pixelSaliency.Count;
        return (int)(Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1) * totalKeyFrame / videoPlayer.frameCount);
    }

    public void ChangedManually() {
        var sceneNumber = Manager.GetActivateSceneNumber();
        if (IfUseKoreaPlusMultiPlusPipMethod())
        {
            multiMethod.VideoNowFrameChangedByUser();
            koreaMethod.InitKoreaMethodPaths();
            pipMethod.VideoChangedByUser();

        }
        else if (IfUseKoreaPlusMultiMethod())
        {
            multiMethod.VideoNowFrameChangedByUser();
            koreaMethod.InitKoreaMethodPaths();
        }
        else if (koreaMethod.isReady)
        {
            koreaMethod.InitKoreaMethodPaths();
        }
        else if (multiMethod.isReady)
        {
            multiMethod.VideoNowFrameChangedByUser();
        }
        UpdateTextMethodStatus(0);
    }

    public void InitKoreaMethod()
    {
        if (pipMethod!=null && pipMethod.isReady)
            pipMethod.Clear();
        if (multiMethod.isReady)
            multiMethod.Clear();

        koreaMethod.Init();
        ChangeTitleName(Manager.productName+" - Single");
        Debug.Log("InitKoreaMethod!");
    }

    public void InitMultiMethod()
    {
        if (koreaMethod.isReady)        
            koreaMethod.Clear();                    
        if (pipMethod!=null && pipMethod.isReady)
            pipMethod.Clear();

        multiMethod.Init();

        ChangeTitleName(Manager.productName+" - Multi");
        Debug.Log("InitMultiMethod!");        
    }
    //
    public void InitKoreaPlusMultiMethod()
    {
        if (pipMethod != null && pipMethod.isReady)
            pipMethod.Clear();

        koreaMethod.Init();        
        multiMethod.Init();

        ChangeTitleName(Manager.productName+" - Single+Multi");
        Debug.Log("InitKoreaPlusMultiMethod!");
    }
    //
    public void InitKoreaPlusMultiPlusPipMethod()
    {

        koreaMethod.Init();
        multiMethod.Init();
        pipMethod.Init();

        ChangeTitleName(Manager.productName+" - Single+Multi+Pip");
        Debug.Log("InitKMPMethod!");        
    }
    public void PanelNFOVOnClick(int camId) {
        if (multiMethod.isReady) {
            multiMethod.ChangeSelectedCamIdByCamId(camId);            
        }
    }
    
    public bool IfUseKoreaPlusMultiMethod() {
        if (koreaMethod == null || multiMethod == null)
            return false;
        return koreaMethod.isReady && multiMethod.isReady;
    }
    public bool IfUseKoreaPlusMultiPlusPipMethod()
    {
        if (koreaMethod == null || multiMethod == null || pipMethod==null)
            return false;
        return koreaMethod.isReady && multiMethod.isReady && pipMethod.isReady;
    }
    //Update method tips in the upper left corner

    public void UpdateTextMethodStatus(int statusId)
    {        
        var textMethodStatus = manager.textMethodStatus;
        textMethodStatus.text = "";
    }
    //Import the following.
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(System.IntPtr hwnd, System.String lpString);
    [DllImport("user32.dll", EntryPoint = "FindWindow")]
    public static extern System.IntPtr FindWindow(System.String className, System.String windowName);

    //Update window name
    public void ChangeTitleName(string newTitleName)
    {
        return;//Temporarily disable modifying form names
        SetWindowTextW(windowPtr, newTitleName);//Set the title text using the window handle. 
    }
    
    public IEnumerator LoadData(string path) {
        //把测试文件夹下分批的文件夹路径读入        
        dontDestroyOnLoad.InitTestDataList();        
        
        menubarController.LoadDataAndExecuteCommand(path);
        while (!menubarController.ifExecuteCommandFinished)
            yield return null;
        var td = new TestData();
        td.StoreData(manager);
        dontDestroyOnLoad.testData = td;   
    }
    //load test data
    public void ButtonLoadTestDataOnClick() {
        
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose dir",
            ShowNewFolderButton = false   
        };
       
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)//Accept path
        {
            var timeStart = DateTime.Now.Ticks;
            path = fb.SelectedPath;
            Debug.Log("path: " + path);

            StartCoroutine(LoadData(path));

            var timeEnd = DateTime.Now.Ticks;
            Debug.Log("LoadDataByOneClick timeCost: " + (timeEnd - timeStart) / 1e7);
            
            panelTestStartCover.transform.Find("ButtonLoadTestData").gameObject.SetActive(false);
        }
        else
        {
            Debug.Log("cancel!");
            return;
        }
        fb.Dispose();        
    }
    
    public void ButtonTestStartOnClick()
    {
        if (dontDestroyOnLoad.testData == null)
        {
            Debug.Log("Please load data first!");
            return;
        }
        else {            
            panelTestStartCover.SetActive(false);                        
            dontDestroyOnLoad.ResetToTestStart();
            dontDestroyOnLoad.PrepareForCurrentRoundTest();
        }
    }
    
    public void ButtonReplayOnClick() {
        
        panelVideoEndCover.SetActive(false);
        videoPlayer.frame = 0;        
        dontDestroyOnLoad.VideoPlayOrReplay();
        panelVideoController.UpdatePauseContinue(true);

        if (panelVideoController.ifBakeWindowAtLast) {
            panelVideoController.userMainCamAngles.Clear();
        }
    }

    public void DropdownOnValueChanged() {
        dontDestroyOnLoad.methodId = userInterfaceDropdown.value;
    }
}
