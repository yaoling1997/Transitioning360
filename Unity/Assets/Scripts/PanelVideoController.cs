using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

public class PanelVideoController : MonoBehaviour
{
    public readonly static KeyCode changeVideoStopKey = KeyCode.C;
    public readonly static KeyCode changeVoiceStatusKey = KeyCode.V;
    public readonly static KeyCode changeVideoPlayStatusKey = KeyCode.B;

    public float fovHalfH;//Half of the horizontal field of view of the camera
    public float fovHalfV;//Half of the camera's vertical field of view
    public bool ifUseHeatMapShow;
    public bool ifUseParulaMapShow;
    public bool ifTextureFilterModePoint;
    public bool ifShowOriginSaliencyMaskOpticalFlow;
    public int TextureMaxLengthScale;//The texture used for display is scaled to a fraction of the original

    public int cameraWindowThickness;//How many pixels are the camera frame

    private readonly Color[] colorList = new Color[] { Color.yellow, Color.yellow, Color.yellow, Color.yellow, Color.yellow, Color.yellow };//相机框的颜色

    private Manager manager;
    
    public float cameraWindowTextureMaxLength;//Displays the maximum edge length (unit pixel) of camerawindowtexture
    private List<Vector2> angles;//(azimuth, pitch angle)    
    private Camera cameraCalculate;//Camera for calculation, not for imaging
    private GameObject positionObj;//Obj for positioning
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private GameObject scrollviewNFOVsContent;
    private double buttonPauseContinueUpdateTime;

    public Button buttonPauseContinue;
    public Button buttonVoice;
    private VideoPlayer videoPlayer;
    public Image videoPixelSaliencyContent;
    public Image videoRegionalSaliencyContent;
    public Image videoOpticalFlowContent;
    public RawImage videoMaskContent;
    public RawImage videoBlendSaliencyMaskContent;
    public Image[] imageCameraWindow;

    public class CameraAngle
    {
        public Vector2 angle;
        public float t;
        public CameraAngle(Vector2 angle, float t)
        {
            this.angle = angle;
            this.t = t;
        }
    }

    public Camera[] cameraNFOVs;//Camera for NFOV video
    public Camera spareCam;//Idle camera for computing

    public GameObject prefabPanelGroupNFOV;
    public GameObject prefabPanelSingleNFOV;

    public List<Texture2D> pixelSaliencyTextureList;//Stores the pixel salience texture of each frame preprocessed for playback
    public List<Texture2D> regionalSaliencyTextureList;//The regional salience texture for each frame preprocessed is stored for playback
    public List<Texture2D> cameraWindowTextureList;//Store the preprocessed camera window texture of each frame for playback
    public List<Texture2D> opticalFlowTextureList;//Store the preprocessed opticalflow of each frame for playback
    public List<Texture2D> pixelMaskTextureList;
    public List<Texture2D> pixelBlendSaliencyMaskTextureList;

    public List<List<Texture2D>> everyCameraPixelSaliencyTextureList;
    public List<List<Texture2D>> everyCameraRegionalSaliencyTextureList;

    private Image[] cameraFrames;
    public Image[] selectedHintFrames;

    public GameObject[] panelNFOV;

    private RawImage[] everyCameraPixelSaliencyContent;

    private RawImage[] everyCameraRegionalSaliencyContent;

    public Slider videoProgressBar;
    public Text videoTimer;

    public int cameraGroupNum;
    public int cameraGroupSize;
    public bool ifDrawCameraWindow;
    public bool ifNormalizeShowSaliency;
    public bool ifOnlyShowSmoothPathCameraWindow;

    public bool ifBakeWindowAtLast;
    public List<Vector2> userMainCamAngles;

    private int exportFrame;
    private bool ifExportSubVideoFinished;
    private string outputPictureDirName;
    private string outputVideoDirName;
    private string outputPictureDir;
    private string outputVideoDir;

    private List<Color> parulaColorList;
    private List<Color> heatmapColorList;


    void Awake()
    {
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        videoPlayer = manager.videoPlayer;
        cameraCalculate = manager.cameraCalculate;
        fovHalfV = cameraCalculate.fieldOfView / 2;
        fovHalfH = GetHorizontalFov(cameraCalculate) / 2;
        ifUseParulaMapShow = true;
        ifUseHeatMapShow = false;
        ifTextureFilterModePoint = true;
        ifShowOriginSaliencyMaskOpticalFlow = true;
        TextureMaxLengthScale = 16;
        cameraWindowThickness = 1;
        parulaColorList = new List<Color>();
        var parulaColorData2019 = GeneralManager.parulaColorData2019;
        for (int i = 0; i < parulaColorData2019.GetLength(0); i++)
            parulaColorList.Add(new Color((float)parulaColorData2019[i, 0], (float)parulaColorData2019[i, 1], (float)parulaColorData2019[i, 2]));
        var heatmapColorList = new List<Color>();
        heatmapColorList.Add(Color.blue);
        heatmapColorList.Add(Color.cyan);
        heatmapColorList.Add(Color.green);
        heatmapColorList.Add(new Color(1, 1, 0));
        heatmapColorList.Add(Color.red);

        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        scrollviewNFOVsContent = manager.scrollviewNFOVsContent;
        positionObj = new GameObject("PositionObj");
        positionObj.transform.position = Vector3.zero;
        cameraWindowTextureList = new List<Texture2D>();
        ifDrawCameraWindow = true;
        ifNormalizeShowSaliency = false;
        ifOnlyShowSmoothPathCameraWindow = true;
        UpdateButtonVoice();
        buttonPauseContinueUpdateTime = 0;

        userMainCamAngles = new List<Vector2>();

        ifBakeWindowAtLast = false;
        var sceneNum = Manager.GetActivateSceneNumber();
        outputPictureDirName = "Picture";
        outputVideoDirName = "Video";
        if (sceneNum == 2 || sceneNum == 3)
        {
            ifBakeWindowAtLast = true;
        }
    }
    // Use this for initialization
    void Start()
    {
        StartCoroutine(PrepareVideo());//Video initialization, must have, otherwise the attribute is chaotic
        if (opticalFlowCamerasController.smoothPath != null && opticalFlowCamerasController.smoothPath.Length > 0)
            UpdateCameraNum(opticalFlowCamerasController.smoothPath.Length);
        else
            UpdateCameraNum(1);
        var sceneNumber = Manager.GetActivateSceneNumber();
        var dontDestroyOnLoadGameObj = GameObject.Find("DontDestroyOnLoad");
        MyDontDestroyOnLoad dontDestroyOnLoad = null;
        if (dontDestroyOnLoadGameObj != null)
            dontDestroyOnLoad = dontDestroyOnLoadGameObj.GetComponent<MyDontDestroyOnLoad>();
        if (sceneNumber == 3 && !dontDestroyOnLoad.IfHasBottomSubWindow())
        {
            foreach (var item in panelNFOV)
            {
                ChangePanelNFOVActive(false);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        UpdatePixelSaliencyContent();
        UpdateRegionalSaliencyContent();
        UpdateOpticalFlowContent();
        UpdateMaskContent();
        UpdateBlendSaliencyMaskContent();
        UpdateImageCameraWindowContent();
        UpdateEveryCameraPixelSaliencyContent();
        UpdateEveryCameraRegionalSaliencyContent();
        UpdateVideoProgressBarAndTimer();

        if (Input.GetKeyDown(changeVideoPlayStatusKey))
        {
            ButtonPauseContinueOnClick();
        }
        if (Input.GetKeyDown(changeVideoStopKey))
        {
            ButtonStopOnClick();
        }
        if (Input.GetKeyDown(changeVoiceStatusKey))
        {
            ButtonVoiceOnClick();
        }
        if (Manager.GetActivateSceneNumber() >= 2)
        {
            if (videoPlayer.isPrepared && videoPlayer.frame >= (int)videoPlayer.frameCount - 8)
            {
                videoPlayer.frame = 0;
                UpdatePauseContinue(false);
                var pvc = manager.panoramaVideoController;
                if (pvc.IfUseKoreaPlusMultiPlusPipMethod())
                {
                    var avePipWindowSize = Vector2.zero;
                    if (pvc.pipWindowRecordTime == 0)
                    {
                        Debug.LogError("no record pip window");
                    }
                    else
                    {
                        avePipWindowSize = pvc.sumPipWindowSize / pvc.pipWindowRecordTime;
                    }                    
                }
                pvc.panelVideoEndCover.SetActive(true);
                
            }
        }
        if (ifBakeWindowAtLast && videoPlayer.isPlaying)
        {
            var frame = videoPlayer.frame;
            for (int i = userMainCamAngles.Count; i <= frame; i++)
                userMainCamAngles.Add(EulerAngleToAngle(manager.mainNFOVCamera.transform.eulerAngles));
        }
    }
    public void ChangePanelNFOVActive(bool active)
    {
        if (panelNFOV == null)
            return;
        foreach (var p in panelNFOV)
            p.SetActive(active);
    }
    public void UpdateCameraNum(int newCamNum)
    {
        cameraGroupNum = newCamNum;
        if (cameraNFOVs != null)
            foreach (var cam in cameraNFOVs)
            {
                if (cam == null)
                    continue;
                var t = cam.targetTexture;
                cam.targetTexture = null;
                Destroy(t);
                Destroy(cam.gameObject);
            }
        if (panelNFOV != null)
        {
            foreach (var item in panelNFOV)
            {
                Destroy(item);
            }
            panelNFOV = null;
        }
        if (selectedHintFrames != null)
            foreach (var item in selectedHintFrames)
            {
                Destroy(item);
            }
        var sceneNumber = Manager.GetActivateSceneNumber();
        if (sceneNumber == 0)
        {
            panelNFOV = new GameObject[cameraGroupNum];
            cameraGroupSize = 3;
            cameraNFOVs = new Camera[cameraGroupNum * cameraGroupSize];
            everyCameraPixelSaliencyContent = new RawImage[cameraGroupNum];
            everyCameraRegionalSaliencyContent = new RawImage[cameraGroupNum];
            cameraFrames = new Image[cameraGroupNum * cameraGroupSize];
            var subPanelNames = new string[] { "PanelNFOVInitialPath", "PanelNFOVFovAwarePath", "PanelNFOVSmoothPath" };
            for (int i = 0; i < cameraGroupNum; i++)
            {
                var panelGroupNFOV = Instantiate(prefabPanelGroupNFOV);
                panelNFOV[i] = panelGroupNFOV;
                panelGroupNFOV.transform.SetParent(scrollviewNFOVsContent.transform);
                everyCameraPixelSaliencyContent[i] = panelGroupNFOV.transform.Find("PanelPixelSaliency").GetComponent<RawImage>();
                everyCameraRegionalSaliencyContent[i] = panelGroupNFOV.transform.Find("PanelRegionalSaliency").GetComponent<RawImage>();
                for (int j = 0; j < cameraGroupSize; j++)
                {
                    int camId = i * cameraGroupSize + j;
                    cameraNFOVs[camId] = Instantiate(cameraCalculate);
                    cameraNFOVs[camId].name = "cameraNFOV" + camId;
                    var t = Instantiate(cameraCalculate.targetTexture);
                    t.name = string.Format("Camera{0}TextureNFOV{1}", i, j);
                    cameraNFOVs[camId].targetTexture = t;
                    var ri = panelGroupNFOV.transform.Find(subPanelNames[j]).GetComponent<RawImage>();
                    ri.texture = t;
                    cameraFrames[camId] = ri.transform.Find("Frame").GetComponent<Image>();
                }
            }

        }
        else if (sceneNumber == 1)
        {
            panelNFOV = new GameObject[cameraGroupNum];
            cameraGroupSize = 1;
            cameraNFOVs = new Camera[cameraGroupNum * cameraGroupSize];
            cameraFrames = new Image[cameraGroupNum * cameraGroupSize];
            selectedHintFrames = new Image[cameraGroupNum * cameraGroupSize];
            for (int i = 0; i < cameraGroupNum; i++)
            {
                int camId = i;
                var panelSingleNFOV = Instantiate(prefabPanelSingleNFOV);
                panelNFOV[i] = panelSingleNFOV;
                panelSingleNFOV.transform.SetParent(scrollviewNFOVsContent.transform);
                cameraNFOVs[camId] = Instantiate(cameraCalculate);
                cameraNFOVs[camId].name = "cameraNFOV" + camId;
                var t = Instantiate(cameraCalculate.targetTexture);
                t.name = string.Format("Camera{0}TextureNFOV", i);
                cameraNFOVs[camId].targetTexture = t;

                var ri = panelSingleNFOV.GetComponent<RawImage>();
                ri.texture = t;
                cameraFrames[camId] = ri.transform.Find("Frame").GetComponent<Image>();
                selectedHintFrames[camId] = ri.transform.Find("SelectedHintFrame").GetComponent<Image>();
            }
        }
        else if (sceneNumber == 2)
        {
            cameraGroupSize = 1;
            cameraNFOVs = new Camera[cameraGroupNum * cameraGroupSize];
            for (int i = 0; i < cameraGroupNum; i++)
            {
                int camId = i;
                cameraNFOVs[camId] = Instantiate(cameraCalculate);
                cameraNFOVs[camId].name = "cameraNFOV" + camId;
                var t = Instantiate(cameraCalculate.targetTexture);
                t.name = string.Format("Camera{0}TextureNFOV", i);
                cameraNFOVs[camId].targetTexture = t;
            }
        }
        else if (sceneNumber == 3)
        {
            panelNFOV = new GameObject[cameraGroupNum];
            cameraGroupSize = 1;
            cameraNFOVs = new Camera[cameraGroupNum * cameraGroupSize];
            cameraFrames = new Image[cameraGroupNum * cameraGroupSize];
            selectedHintFrames = new Image[cameraGroupNum * cameraGroupSize];
            for (int i = 0; i < cameraGroupNum; i++)
            {
                int camId = i;
                var panelSingleNFOV = Instantiate(prefabPanelSingleNFOV);
                var panelNFOVController = panelSingleNFOV.GetComponent<PanelNFOVController>();
                panelNFOVController.id = camId;
                panelNFOV[i] = panelSingleNFOV;
                panelSingleNFOV.transform.SetParent(scrollviewNFOVsContent.transform);
                panelSingleNFOV.transform.localScale = new Vector3(1, 1, 1);
                cameraNFOVs[camId] = Instantiate(cameraCalculate);
                cameraNFOVs[camId].name = "cameraNFOV" + camId;
                var t = Instantiate(cameraCalculate.targetTexture);
                t.name = string.Format("Camera{0}TextureNFOV", i);
                cameraNFOVs[camId].targetTexture = t;

                var ri = panelSingleNFOV.GetComponent<RawImage>();
                ri.texture = t;
                cameraFrames[camId] = ri.transform.Find("Frame").GetComponent<Image>();
                selectedHintFrames[camId] = ri.transform.Find("SelectedHintFrame").GetComponent<Image>();
            }
        }

        spareCam = Instantiate(cameraCalculate);
        spareCam.name = "cameraSpare";
        var tex = Instantiate(cameraCalculate.targetTexture);
        tex.name = string.Format("CameraSpareTextureNFOV");
        spareCam.targetTexture = tex;

        PrepareNFOVFrameTextures();
    }

    //Get the horizontal view angle of the camera
    public float GetHorizontalFov(Camera cam)
    {
        var radAngle = cam.fieldOfView * Mathf.Deg2Rad;
        var radHFOV = 2 * Mathf.Atan(Mathf.Tan(radAngle / 2) * cam.aspect);
        var hFOV = Mathf.Rad2Deg * radHFOV;
        return hFOV;
    }
    private void UpdatePixelSaliencyContent()
    {
        if (videoPlayer.frameCount == 0 || videoPixelSaliencyContent == null)
            return;
        var frame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * pixelSaliencyTextureList.Count);
        if (frame < pixelSaliencyTextureList.Count && videoPlayer.isPlaying)
        {
            var t = pixelSaliencyTextureList[frame];
            videoPixelSaliencyContent.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2());
        }
    }
    private void UpdateMaskContent()
    {
        if (videoPlayer.frameCount == 0 || videoMaskContent == null)
            return;
        var frame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * pixelMaskTextureList.Count);
        if (frame < pixelMaskTextureList.Count && videoPlayer.isPlaying)
        {
            var t = pixelMaskTextureList[frame];
            videoMaskContent.texture = t;
        }
    }
    private void UpdateBlendSaliencyMaskContent()
    {
        if (videoPlayer.frameCount == 0 || videoBlendSaliencyMaskContent == null)
            return;
        var frame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * pixelBlendSaliencyMaskTextureList.Count);
        if (frame < pixelBlendSaliencyMaskTextureList.Count && videoPlayer.isPlaying)
        {
            var t = pixelBlendSaliencyMaskTextureList[frame];
            videoBlendSaliencyMaskContent.texture = t;
        }
    }

    private void UpdateEveryCameraPixelSaliencyContent()
    {
        if (everyCameraPixelSaliencyTextureList == null || everyCameraPixelSaliencyTextureList.Count == 0)
            return;
        var cameraGroupNum = cameraNFOVs.Length / 3;
        for (int camId = 0; camId < cameraGroupNum; camId++)
        {
            var pixelSaliencyTextureList = everyCameraPixelSaliencyTextureList[camId];
            if (pixelSaliencyTextureList == null || pixelSaliencyTextureList.Count == 0)
                continue;
            var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * pixelSaliencyTextureList.Count);
            if (keyFrame < pixelSaliencyTextureList.Count && videoPlayer.isPlaying)
            {
                var t = pixelSaliencyTextureList[keyFrame];
                everyCameraPixelSaliencyContent[camId].texture = t;
            }
        }
    }
    private void UpdateEveryCameraRegionalSaliencyContent()//Update the content displayed by regional salience content
    {
        if (everyCameraRegionalSaliencyTextureList == null || everyCameraRegionalSaliencyTextureList.Count == 0)
            return;
        var cameraGroupNum = cameraNFOVs.Length / 3;
        for (int camId = 0; camId < cameraGroupNum; camId++)
        {
            var regionalSaliencyTextureList = everyCameraRegionalSaliencyTextureList[camId];
            if (regionalSaliencyTextureList == null || regionalSaliencyTextureList.Count == 0)
                continue;
            var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * regionalSaliencyTextureList.Count);
            if (keyFrame < regionalSaliencyTextureList.Count && videoPlayer.isPlaying)
            {
                var t = regionalSaliencyTextureList[keyFrame];
                everyCameraRegionalSaliencyContent[camId].texture = t;
            }
        }
    }
    public void VideoProgressBarOnDrag()
    {
        videoPlayer.frame = (long)(videoProgressBar.value * videoPlayer.frameCount);
        var sceneNumber = Manager.GetActivateSceneNumber();
        if (sceneNumber == 1)
            manager.mainNFOVController.VideoNowFrameChangedByUser();
        else if (sceneNumber >= 2)
        {
            manager.panoramaVideoController.ChangedManually();
        }
    }
    private string TimeNumberToString(double t)
    {
        var tmp = (int)t;
        var ss = string.Format("{0:D2}", tmp % 60);
        tmp /= 60;
        var mm = string.Format("{0:D2}", tmp % 60);
        tmp /= 60;
        var hh = string.Format("{0:D2}", tmp % 60);
        tmp /= 60;
        return string.Format("{0}:{1}:{2}", hh, mm, ss);
    }
    private void UpdateVideoProgressBarAndTimer()
    {
        var percentage = (float)videoPlayer.frame / videoPlayer.frameCount;
        videoProgressBar.value = percentage;
        var nowTime = TimeNumberToString(videoPlayer.time);
        var totalTime = TimeNumberToString(videoPlayer.frameCount / videoPlayer.frameRate);
        videoTimer.text = nowTime + " / " + totalTime;
    }
    private void UpdateRegionalSaliencyContent()//Update the content displayed by regional salience content
    {
        if (regionalSaliencyTextureList == null || regionalSaliencyTextureList.Count == 0 || videoRegionalSaliencyContent == null)
            return;
        var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * regionalSaliencyTextureList.Count);
        if (keyFrame < regionalSaliencyTextureList.Count && videoPlayer.isPlaying)
        {
            var t = regionalSaliencyTextureList[keyFrame];
            videoRegionalSaliencyContent.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2());
        }
    }
    private void UpdateImageCameraWindowContent()//Update the content displayed in imagecamerawindow
    {
        if (cameraWindowTextureList != null && videoPlayer.frame < cameraWindowTextureList.Count && videoPlayer.isPlaying)
        {
            var t = cameraWindowTextureList[(int)videoPlayer.frame];
            foreach (var item in imageCameraWindow)
                item.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2());
        }
    }
    private void UpdateOpticalFlowContent()//Update the contents of the opticalflowtexture display
    {
        if (opticalFlowTextureList == null || opticalFlowTextureList.Count == 0 || videoOpticalFlowContent == null)
            return;
        var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * opticalFlowTextureList.Count);
        if (keyFrame < opticalFlowTextureList.Count && videoPlayer.isPlaying)
        {
            var t = opticalFlowTextureList[keyFrame];
            videoOpticalFlowContent.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2());
        }
    }
    public Color ValueToColor(float v)
    {
        v = Mathf.Clamp(v, 0, 1);
        List<Color> colorList = null;
        if (ifUseParulaMapShow) {
            colorList = parulaColorList;
        }
        else if (ifUseHeatMapShow)
        {
            colorList = heatmapColorList;
        }
        else
        {
            return new Color(v, v, v);
        }

        int segNum = colorList.Count - 1;
        float vSeg = 1f / segNum;
        int colorId = Mathf.Min((int)(v / vSeg), segNum - 1);
        v -= colorId * vSeg;
        var c1 = colorList[colorId];
        var c2 = colorList[colorId + 1];
        var p1 = new Vector3(c1.r, c1.g, c1.b);
        var p2 = new Vector3(c2.r, c2.g, c2.b);
        var p = p1 + (p2 - p1) * v / vSeg;
        return new Color(p.x, p.y, p.z);
    }
    public Texture2D VisualizeSaliencyOrMask(float[,] data, int width, int height, bool ifNormalize)
    {
        var re = new Texture2D(width, height);
        if (ifTextureFilterModePoint)
            re.filterMode = FilterMode.Point;
        float maxV = 0;
        if (ifNormalize)
            for (int u = 0; u < width; u++)
                for (int v = 0; v < height; v++)
                    if (maxV < data[u, v])
                        maxV = data[u, v];
        for (int u = 0; u < width; u++)
            for (int v = 0; v < height; v++)
            {
                var x = data[u, v];
                if (ifNormalize && maxV > 0)
                    x /= maxV;
                var c = ValueToColor(x);
                re.SetPixel(u, height - 1 - v, c);//The unity pixel coordinate V is upward, starting from 1
            }
        re.Apply();
        return re;
    }

    public void PrepareMaskTextures()
    {
        Manager.DestroyTexture2dList(pixelMaskTextureList);
        pixelMaskTextureList = new List<Texture2D>();
        int tw = 0, th = 0;
        List<float[,]> pixelMaskList = null;
        if (ifShowOriginSaliencyMaskOpticalFlow)
        {
            tw = MenuBarController.pixelMaskWidth;
            th = MenuBarController.pixelMaskHeight;
            pixelMaskList = opticalFlowCamerasController.pixelMaskList;
        }
        else
        {
            opticalFlowCamerasController.GetDownsampleSize(out tw, out th);
            pixelMaskList = opticalFlowCamerasController.GetDownsamplePixelMask(tw, th, true);
        }
        if (pixelMaskList == null || pixelMaskList.Count == 0)
            return;
        foreach (var pixelMask in pixelMaskList)
        {
            var t = VisualizeSaliencyOrMask(pixelMask, tw, th, ifNormalizeShowSaliency);
            pixelMaskTextureList.Add(t);
        }
        Debug.Log("pixelMaskList.size: " + pixelMaskList.Count);
        Debug.Log("pixelMaskTextureList.size: " + pixelMaskTextureList.Count);
    }
    public void PreparePixelBlendSaliencyMaskTextures()
    {
        Manager.DestroyTexture2dList(pixelBlendSaliencyMaskTextureList);
        pixelBlendSaliencyMaskTextureList = new List<Texture2D>();
        int tw = 0, th = 0;
        List<float[,]> pixelBlendList = null;
        if (ifShowOriginSaliencyMaskOpticalFlow)
        {
            tw = MenuBarController.pixelSaliencyWidth;
            th = MenuBarController.pixelSaliencyHeight;
            var ps = opticalFlowCamerasController.ifUseNormalizationPixelSaliency ? opticalFlowCamerasController.normalizedPixelSaliency : opticalFlowCamerasController.pixelSaliency;
            pixelBlendList = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(ps, opticalFlowCamerasController.pixelMaskList, tw, th);
        }
        else
        {
            opticalFlowCamerasController.GetDownsampleSize(out tw, out th);
            pixelBlendList = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(tw, th, opticalFlowCamerasController.ifUseNormalizationPixelSaliency);
        }
        if (pixelBlendList == null)
            return;
        foreach (var pixelBlend in pixelBlendList)
        {
            var t = VisualizeSaliencyOrMask(pixelBlend, tw, th, ifNormalizeShowSaliency);
            pixelBlendSaliencyMaskTextureList.Add(t);
        }
    }
    public void PreparePixelSaliencyTextures()
    {
        Manager.DestroyTexture2dList(pixelSaliencyTextureList);
        pixelSaliencyTextureList = new List<Texture2D>();
        int tw = 0, th = 0;
        List<float[,]> pixelSaliencyList = null;
        if (ifShowOriginSaliencyMaskOpticalFlow)
        {
            tw = MenuBarController.pixelSaliencyWidth;
            th = MenuBarController.pixelSaliencyHeight;
            pixelSaliencyList = new List<float[,]>();
            var oc = opticalFlowCamerasController;
            var originPixelSaliency = oc.ifUseNormalizationPixelSaliency ? oc.normalizedPixelSaliency : oc.pixelSaliency;
            foreach (var ps in originPixelSaliency)
            {
                var tmp = new float[tw, th];
                for (int i = 0; i < tw; i++)
                    for (int j = 0; j < th; j++)
                        tmp[i, j] = Mathf.Min(ps[i, j] * opticalFlowCamerasController.pixelSaliencyEnhanceWeight, 1);
                pixelSaliencyList.Add(tmp);
            }
        }
        else
        {
            opticalFlowCamerasController.GetDownsampleSize(out tw, out th);
            pixelSaliencyList = opticalFlowCamerasController.GetDownsamplePixelSaliency(tw, th, ifNormalizeShowSaliency);
        }
        if (pixelSaliencyList == null || pixelSaliencyList.Count == 0)
            return;
        foreach (var pixelSaliency in pixelSaliencyList)
        {
            var t = VisualizeSaliencyOrMask(pixelSaliency, tw, th, ifNormalizeShowSaliency);
            pixelSaliencyTextureList.Add(t);
        }
        Debug.Log("saliencyList.size: " + pixelSaliencyList.Count);
        Debug.Log("saliencyTextureList.size: " + pixelSaliencyTextureList.Count);
    }
    public void PrepareRegionalSaliencyTextures()
    {
        Manager.DestroyTexture2dList(regionalSaliencyTextureList);
        regionalSaliencyTextureList = new List<Texture2D>();
        var regionalSaliencyList = opticalFlowCamerasController.regionalSaliency;
        if (regionalSaliencyList == null || regionalSaliencyList.Count == 0)
            return;
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var width = downsampleWidth;
        var height = downsampleHeight;
        foreach (var regionalSaliency in regionalSaliencyList)
        {
            var t = VisualizeSaliencyOrMask(regionalSaliency, width, height, ifNormalizeShowSaliency);
            regionalSaliencyTextureList.Add(t);
        }
        Debug.Log("regionalSaliencyTextureList.Count: " + regionalSaliencyTextureList.Count);
    }

    public Texture2D VisualizeOpticalFlow(Vector2[,] flow, int width, int height)
    {
        var tmp = new float[width, height][];
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
                tmp[i, j] = new float[] { flow[i, j].x, flow[i, j].y };
        return VisualizeOpticalFlow(tmp, width, height);
    }

    public Texture2D VisualizeOpticalFlow(float[,][] flow, int width, int height)
    {
        var re = new Texture2D(width, height);
        if (ifTextureFilterModePoint)
            re.filterMode = FilterMode.Point;
        byte ncols;

        ushort RY = 15;
        ushort YG = 6;
        ushort GC = 4;
        ushort CB = 11;
        ushort BM = 13;
        ushort MR = 6;
        ncols = (byte)(RY + YG + GC + CB + BM + MR);
        var colorwheel = new float[55, 3];
        //ushort nchans = 3;
        ushort col = 0;
        //RY
        for (int i = 0; i < RY; i++)
        {
            colorwheel[col + i, 0] = 255;
            colorwheel[col + i, 1] = 255 * i / RY;
            colorwheel[col + i, 2] = 0;
            //std::cout << colorwheel[i][1] << '\n';
        }
        col += RY;
        //YG
        for (int i = 0; i < YG; i++)
        {
            colorwheel[col + i, 0] = 255 - 255 * i / YG;
            colorwheel[col + i, 1] = 255;
            colorwheel[col + i, 2] = 0;
        }
        col += YG;
        //GC
        for (int i = 0; i < GC; i++)
        {
            colorwheel[col + i, 1] = 255;
            colorwheel[col + i, 2] = 255 * i / GC;
            colorwheel[col + i, 0] = 0;
        }
        col += GC;
        //CB
        for (int i = 0; i < CB; i++)
        {
            colorwheel[col + i, 1] = 255 - 255 * i / CB;
            colorwheel[col + i, 2] = 255;
            colorwheel[col + i, 0] = 0;
        }
        col += CB;
        //BM
        for (int i = 0; i < BM; i++)
        {
            colorwheel[col + i, 2] = 255;
            colorwheel[col + i, 0] = 255 * i / BM;
            colorwheel[col + i, 1] = 0;
        }
        col += BM;
        //MR
        for (int i = 0; i < MR; i++)
        {
            colorwheel[col + i, 2] = 255 - 255 * i / MR;
            colorwheel[col + i, 0] = 255;
            colorwheel[col + i, 1] = 0;
        }

        float UNKNOWN_THRESH = 1e5f;
        float max_norm = 1e-10f;
        //compute the max norm
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                var data = flow[i, j];
                float u = data[0];
                float v = data[1];
                float norm = Mathf.Sqrt(u * u + v * v);
                if (norm > UNKNOWN_THRESH)
                {
                    data[0] = 0;
                    data[1] = 0;
                }
                else if (norm > max_norm)
                {
                    max_norm = norm;
                }
            }
        }
        //calculate the rgb value
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                var data = flow[i, j];
                var img_data = new float[3];
                float u = data[0];
                float v = data[1];
                float norm = Mathf.Sqrt(u * u + v * v) / max_norm;
                float angle = Mathf.Atan2(-v, -u) / Mathf.PI;
                float fk = (angle + 1) / 2 * ((float)(ncols) - 1);
                int k0 = (int)Mathf.Floor(fk);
                int k1 = k0 + 1;
                if (k1 == ncols)
                {
                    k1 = 0;
                }
                float f = fk - k0;
                for (int k = 0; k < 3; k++)
                {
                    float col0 = (colorwheel[k0, k] / 255);
                    float col1 = (colorwheel[k1, k] / 255);
                    float col3 = (1 - f) * col0 + f * col1;
                    if (norm <= 1)
                    {
                        col3 = 1 - norm * (1 - col3);
                    }
                    else
                    {
                        col3 *= 0.75f;
                    }
                    //img_data[k] = (byte)(255 * col3);
                    img_data[k] = col3;
                }
                re.SetPixel(i, height - 1 - j, new Color(img_data[0], img_data[1], img_data[2]));
            }
        }
        re.Apply();
        return re;
    }

    public void PrepareOpticalFlowTextures()
    {
        Manager.DestroyTexture2dList(opticalFlowTextureList);
        opticalFlowTextureList = new List<Texture2D>();
        int tw = 0, th = 0;
        List<Vector2[,]> pixelOpticalFlowList = null;
        if (ifShowOriginSaliencyMaskOpticalFlow)
        {
            tw = MenuBarController.pixelOpticalFlowWidth;
            th = MenuBarController.pixelOpticalFlowHeight;
            pixelOpticalFlowList = opticalFlowCamerasController.pixelOpticalFlow;
        }
        else
        {
            opticalFlowCamerasController.GetDownsampleSize(out tw, out th);
            pixelOpticalFlowList = opticalFlowCamerasController.GetDownsamplePixelOpticalFlow(tw, th, true);
        }
        //Debug.Log(string.Format("tw,th:{0},{1}", tw, th));        
        foreach (var opticalFlow in pixelOpticalFlowList)
        {
            var t = VisualizeOpticalFlow(opticalFlow, tw, th);
            opticalFlowTextureList.Add(t);
        }
    }

    public void PrepareEveryCameraPixelSaliencyTextures()
    {
        Manager.DestroyTexture2dList(everyCameraPixelSaliencyTextureList);
        everyCameraPixelSaliencyTextureList = new List<List<Texture2D>>();
        var everyCameraPixelSaliency = opticalFlowCamerasController.everyCameraDownsamplePixelSaliency;
        if (everyCameraPixelSaliency == null || everyCameraPixelSaliency.Count == 0)
            return;
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var width = downsampleWidth;
        var height = downsampleHeight;
        if (ifShowOriginSaliencyMaskOpticalFlow)
        {
            width = MenuBarController.pixelSaliencyWidth;
            height = MenuBarController.pixelSaliencyHeight;
        }
        var cameraGroupNum = cameraNFOVs.Length / 3;
        for (int camId = 0; camId < cameraGroupNum; camId++)
        {
            var pixelSaliencyTextureList = new List<Texture2D>();
            foreach (var pixelSaliency in everyCameraPixelSaliency[camId])
            {
                var t = VisualizeSaliencyOrMask(pixelSaliency, width, height, ifNormalizeShowSaliency);
                pixelSaliencyTextureList.Add(t);
            }
            everyCameraPixelSaliencyTextureList.Add(pixelSaliencyTextureList);
            //Debug.Log("regionalSaliencyTextureList.Count: " + regionalSaliencyTextureList.Count);
        }
    }
    public void PrepareEveryCameraRegionalSaliencyTextures()
    {
        Manager.DestroyTexture2dList(everyCameraRegionalSaliencyTextureList);
        everyCameraRegionalSaliencyTextureList = new List<List<Texture2D>>();
        var everyCameraRegionalSaliency = opticalFlowCamerasController.everyCameraDownsampleRegionalSaliency;
        if (everyCameraRegionalSaliency == null || everyCameraRegionalSaliency.Count == 0)
            return;
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var width = downsampleWidth;
        var height = downsampleHeight;
        var cameraGroupNum = cameraNFOVs.Length / 3;
        for (int camId = 0; camId < cameraGroupNum; camId++)
        {
            var regionalSaliencyTextureList = new List<Texture2D>();
            foreach (var regionalSaliency in everyCameraRegionalSaliency[camId])
            {
                var t = VisualizeSaliencyOrMask(regionalSaliency, width, height, ifNormalizeShowSaliency);
                regionalSaliencyTextureList.Add(t);
            }
            everyCameraRegionalSaliencyTextureList.Add(regionalSaliencyTextureList);
            //Debug.Log("regionalSaliencyTextureList.Count: " + regionalSaliencyTextureList.Count);
        }
    }

    private bool IfPixelOnBorder(int x, int y, int width, int height)
    {
        var w = 10;
        return x < w || x > width - 1 - w || y < w || y > height - 1 - w;
    }

    public void AddColorFrameToImage(Image image, int width, int height, Color color)
    {
        var t = new Texture2D(width, height);
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
                if (IfPixelOnBorder(i, j, width, height))
                {
                    t.SetPixel(i, j, color);
                }
                else
                {
                    t.SetPixel(i, j, Color.clear);
                }
        t.Apply();
        image.sprite = Sprite.Create(t, new Rect(0, 0, width, height), Vector2.zero);
    }
    public void PrepareNFOVFrameTextures()
    {
        if (cameraFrames == null)
            return;
        var width = cameraCalculate.targetTexture.width;
        var height = cameraCalculate.targetTexture.height;
        Debug.Log(string.Format("Frame width,height: ({0},{1})", width, height));
        var sceneNumber = Manager.GetActivateSceneNumber();
        for (int id = 0; id < cameraFrames.Length; id++)
        {
            var c = Color.clear;
            if (sceneNumber == 0)
                c = colorList[id % 3];
            else if (sceneNumber == 1 || sceneNumber == 3)
                c = colorList[id];
            AddColorFrameToImage(cameraFrames[id], width, height, c);
            if (sceneNumber == 1 || sceneNumber == 3)
                AddColorFrameToImage(selectedHintFrames[id], width, height, Color.red);
        }
    }


    private void DrawCameraWindowToTexture2D(Camera[] cameraList, List<Vector2>[] camerasAngles, int frame, int textureWidth, int textureHeight, Texture2D t, ref int[] j, Color[] newColorList)
    {
        var videoWidth = videoPlayer.targetTexture.width;
        var videoHeight = videoPlayer.targetTexture.height;
        //var cameraList = cameraNFOVs;
        var percentage = (float)frame / videoPlayer.frameCount;
        float wSeg = videoWidth / textureWidth;
        float hSeg = videoHeight / textureHeight;
        int beginCamId = 0;
        int camIdInc = 1;
        if (Manager.GetActivateSceneNumber() == 0 && ifOnlyShowSmoothPathCameraWindow)
        {
            beginCamId = 2;
            camIdInc = 3;
        }

        for (int cameraId = beginCamId; cameraId < cameraList.Length; cameraId += camIdInc)
        {
            var cameraAngles = camerasAngles[cameraId];

            //var cameraTmp = cameraList[cameraId];
            var cameraTmp = spareCam;
            if (cameraAngles.Count == 0)
            {
                cameraTmp.transform.eulerAngles = Vector3.zero;
            }
            else
            {
                if (ifBakeWindowAtLast && cameraId == cameraList.Length - 1)
                {
                    
                    j[cameraId] = frame;
                    cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[frame]);
                }
                else
                {
                    while (j[cameraId] + 1 < cameraAngles.Count && ((float)j[cameraId] + 1) / cameraAngles.Count <= percentage)
                        j[cameraId]++;
                    var p = j[cameraId];
                    if (p + 1 == cameraAngles.Count || cameraAngles[p].Equals(cameraAngles[p + 1]))
                    {
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p]);
                    }
                    else
                    {
                        var tl = (float)p / cameraAngles.Count;
                        var tr = ((float)p + 1) / cameraAngles.Count;
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p]);
                        var forwardStart = cameraTmp.transform.forward;
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p + 1]);
                        var forwardEnd = cameraTmp.transform.forward;
                        var theta = Vector3.Angle(forwardStart, forwardEnd) * (percentage - tl) / (tr - tl);
                        theta = theta * Mathf.Deg2Rad;
                        var forward = Vector3.RotateTowards(forwardStart, forwardEnd, theta, 1);
                        cameraTmp.transform.forward = forward;
                    }
                }
            }
            var chosenColor = Color.clear;
            if (Manager.GetActivateSceneNumber() == 0)
                chosenColor = colorList[cameraId / 3];
            else if (Manager.GetActivateSceneNumber() == 1)
                chosenColor = colorList[cameraId];
            else
            {
                chosenColor = newColorList[cameraId];
            }
            var vis = new int[textureWidth, textureHeight];
            int timeStep = 1;
            var pixels = new List<Vector2>();
            for (int u = 0; u < textureWidth; u++)
            {
                var uu = u * wSeg + wSeg / 2;
                for (int v = 0; v < textureHeight; v++)
                {//up                                                
                    var vv = v * hSeg + hSeg / 2;
                    var angle = PixelToAngle(new Vector2(uu, vv));
                    var v3 = EulerAngleToVector3(AngleToEulerAngle(angle));
                    var p = cameraTmp.WorldToViewportPoint(v3);
                    if (p.z >= cameraTmp.nearClipPlane && 0 <= p.x && p.x <= 1 && 0 <= p.y && p.y <= 1)
                    {
                        vis[u, v] = timeStep;
                        pixels.Add(new Vector2(u, v));
                    }
                }
            }
            var move = new Vector2[] {
                    new Vector2(1,0),
                    new Vector2(-1,0),
                    new Vector2(0,1),
                    new Vector2(0,-1)
                };
            var showPixelList = new List<Vector2>();
            var selectedPixelList = new List<Vector2>();
            var unselectedPixelList = new List<Vector2>();

            foreach (var p in pixels)
            {
                var bl = false;
                foreach (var m in move)
                {
                    int u = (int)p.x + (int)m.x;
                    int v = (int)p.y + (int)m.y;
                    OpticalFlowCamerasController.NormalizePixelInRange(ref u, ref v, textureWidth, textureHeight);
                    if (vis[u, v] != timeStep /*|| (p.x == u && p.y == v)*/)
                    {
                        bl = true;
                        break;
                    }
                }
                if (bl)
                    selectedPixelList.Add(p);
            }
            foreach (var p in selectedPixelList)
            {
                vis[(int)p.x, (int)p.y] = -1;
                showPixelList.Add(p);
            }
            for (timeStep++; timeStep <= cameraWindowThickness; timeStep++)
            {
                pixels = selectedPixelList;
                selectedPixelList = new List<Vector2>();
                foreach (var p in pixels)
                {
                    foreach (var m in move)
                    {
                        int u = (int)p.x + (int)m.x;
                        int v = (int)p.y + (int)m.y;
                        OpticalFlowCamerasController.NormalizePixelInRange(ref u, ref v, textureWidth, textureHeight);

                        if (vis[u, v] > 0)//是未被选择的元素
                        {
                            selectedPixelList.Add(new Vector2(u, v));
                            vis[u, v] = -1;
                        }
                    }
                }

                foreach (var p in selectedPixelList)
                {
                    showPixelList.Add(p);
                }
            }
            foreach (var p in showPixelList)
            {
                int u = (int)p.x;
                int v = (int)p.y;
                t.SetPixel(u, textureHeight - 1 - v, chosenColor);
            }
        }
    }
    private void DrawCameraWindowAndRectToTexture2DOnlyForKeyframe(Camera[] cameraList, List<Vector2>[] camerasAngles, int frame, int textureWidth, int textureHeight, Texture2D t, ref int[] j, Color[] newColorList, List<Vector2>[] pathList)
    {
        var videoWidth = videoPlayer.targetTexture.width;
        var videoHeight = videoPlayer.targetTexture.height;
        var percentage = (float)frame / videoPlayer.frameCount;
        float wSeg = videoWidth / textureWidth;
        float hSeg = videoHeight / textureHeight;
        int beginCamId = 0;
        int camIdInc = 1;
        if (Manager.GetActivateSceneNumber() == 0 && ifOnlyShowSmoothPathCameraWindow)
        {
            beginCamId = 2;
            camIdInc = 3;
        }

        for (int cameraId = beginCamId; cameraId < cameraList.Length; cameraId += camIdInc)
        {
            var cameraAngles = camerasAngles[cameraId];

            var cameraTmp = spareCam;
            if (cameraAngles.Count == 0)
            {
                cameraTmp.transform.eulerAngles = Vector3.zero;
            }
            else
            {
                if (ifBakeWindowAtLast && cameraId == cameraList.Length - 1)
                {
                    j[cameraId] = frame;
                    cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[frame]);
                }
                else
                {
                    while (j[cameraId] + 1 < cameraAngles.Count && ((float)j[cameraId] + 1) / cameraAngles.Count <= percentage)
                        j[cameraId]++;
                    var p = j[cameraId];
                    if (p + 1 == cameraAngles.Count || cameraAngles[p].Equals(cameraAngles[p + 1]))
                    {
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p]);
                    }
                    else
                    {
                        var tl = (float)p / cameraAngles.Count;
                        var tr = ((float)p + 1) / cameraAngles.Count;
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p]);
                        var forwardStart = cameraTmp.transform.forward;
                        cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p + 1]);
                        var forwardEnd = cameraTmp.transform.forward;
                        var theta = Vector3.Angle(forwardStart, forwardEnd) * (percentage - tl) / (tr - tl);
                        theta = theta * Mathf.Deg2Rad;
                        var forward = Vector3.RotateTowards(forwardStart, forwardEnd, theta, 1);
                        cameraTmp.transform.forward = forward;
                    }
                    cameraTmp.transform.eulerAngles = AngleToEulerAngle(cameraAngles[p]);
                }
            }
            var chosenColor = Color.clear;
            if (Manager.GetActivateSceneNumber() == 0)
                chosenColor = colorList[cameraId % 3];
            else if (Manager.GetActivateSceneNumber() == 1)
                chosenColor = colorList[cameraId];
            else
            {
                chosenColor = newColorList[cameraId];
            }
            chosenColor = Color.black;
            var vis = new int[textureWidth, textureHeight];
            int timeStep = 1;
            var pixels = new List<Vector2>();
            for (int u = 0; u < textureWidth; u++)
            {
                var uu = u * wSeg + wSeg / 2;
                for (int v = 0; v < textureHeight; v++)
                {//up                                                
                    var vv = v * hSeg + hSeg / 2;
                    var angle = PixelToAngle(new Vector2(uu, vv));
                    var v3 = EulerAngleToVector3(AngleToEulerAngle(angle));
                    var p = cameraTmp.WorldToViewportPoint(v3);
                    if (p.z >= cameraTmp.nearClipPlane && 0 <= p.x && p.x <= 1 && 0 <= p.y && p.y <= 1)
                    {
                        vis[u, v] = timeStep;
                        pixels.Add(new Vector2(u, v));
                    }
                }
            }
            var move = new Vector2[] {
                    new Vector2(1,0),
                    new Vector2(-1,0),
                    new Vector2(0,1),
                    new Vector2(0,-1)
                };
            var showPixelList = new List<Vector2>();
            var selectedPixelList = new List<Vector2>();
            var unselectedPixelList = new List<Vector2>();

            foreach (var p in pixels)
            {
                var bl = false;
                foreach (var m in move)
                {
                    int u = (int)p.x + (int)m.x;
                    int v = (int)p.y + (int)m.y;
                    OpticalFlowCamerasController.NormalizePixelInRange(ref u, ref v, textureWidth, textureHeight);
                    if (vis[u, v] != timeStep /*|| (p.x == u && p.y == v)*/)
                    {
                        bl = true;
                        break;
                    }
                }
                if (bl)
                    selectedPixelList.Add(p);
            }
            foreach (var p in selectedPixelList)
            {
                vis[(int)p.x, (int)p.y] = -1;
                showPixelList.Add(p);
            }
            for (timeStep++; timeStep <= cameraWindowThickness; timeStep++)
            {
                pixels = selectedPixelList;
                selectedPixelList = new List<Vector2>();
                foreach (var p in pixels)
                {
                    foreach (var m in move)
                    {
                        int u = (int)p.x + (int)m.x;
                        int v = (int)p.y + (int)m.y;
                        OpticalFlowCamerasController.NormalizePixelInRange(ref u, ref v, textureWidth, textureHeight);

                        if (vis[u, v] > 0)
                        {
                            selectedPixelList.Add(new Vector2(u, v));
                            vis[u, v] = -1;
                        }
                    }
                }

                foreach (var p in selectedPixelList)
                {
                    showPixelList.Add(p);
                }
            }
            foreach (var p in showPixelList)
            {
                int u = (int)p.x;
                int v = (int)p.y;
                t.SetPixel(u, textureHeight - 1 - v, chosenColor);
            }
        }
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        for (int cameraId = beginCamId; cameraId < cameraList.Length; cameraId += camIdInc) {
            var pos = pathList[cameraId][j[cameraId]];
            var rectOfFOV = opticalFlowCamerasController.rectOfPixel[(int)pos.x, (int)pos.y];
            var pMin = rectOfFOV.min;
            var pMax = rectOfFOV.max;
            pMax.y -= 0.5f;
            pMin = opticalFlowCamerasController.PixelToOriginPixelFloatValue(pMin, new Vector2(downsampleWidth, downsampleHeight), new Vector2(textureWidth, textureHeight));
            pMax = opticalFlowCamerasController.PixelToOriginPixelFloatValue(pMax, new Vector2(downsampleWidth, downsampleHeight), new Vector2(textureWidth, textureHeight));
            for (int i = (int)pMin.x; i <= pMax.x; i++)
            {
                int xx = i;
                int yy = (int)pMin.y;
                OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, textureWidth, textureHeight);
                t.SetPixel(xx, textureHeight - 1 - yy, Color.red);
                yy = (int)pMax.y;
                OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, textureWidth, textureHeight);
                t.SetPixel(xx, textureHeight - 1 - yy, Color.red);
            }
            for (int i = (int)pMin.y; i <= pMax.y; i++)
            {
                int yy = i;
                int xx = (int)pMin.x;
                OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, textureWidth, textureHeight);
                t.SetPixel(xx, textureHeight - 1 - yy, Color.red);
                xx = (int)pMax.x;
                OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, textureWidth, textureHeight);
                t.SetPixel(xx, textureHeight - 1 - yy, Color.red);
            }
            t.Apply();
        }            
    }
    //Generating camera window texture based on the motion track of opticalflow camera
    public void PrepareCameraOpticalFlowWindowTextures()
    {
        if (!ifBakeWindowAtLast && Manager.GetActivateSceneNumber() >= 1)
            return;

        var timeStart = DateTime.Now.Ticks;
        UpdateTextureMaxLength();
        
        Manager.DestroyTexture2dList(cameraWindowTextureList);
        cameraWindowTextureList = new List<Texture2D>();
        var videoWidth = videoPlayer.targetTexture.width;
        var videoHeight = videoPlayer.targetTexture.height;
        var textureWidth = (int)Mathf.Min(cameraWindowTextureMaxLength, videoWidth * cameraWindowTextureMaxLength / videoHeight);
        var textureHeight = (int)Mathf.Min(cameraWindowTextureMaxLength, videoHeight * cameraWindowTextureMaxLength / videoWidth);

        var cameraList = cameraNFOVs;
        var pathList = new List<Vector2>[cameraList.Length];
        for (int i = 0; i < cameraGroupNum; i++)
        {
            if (Manager.GetActivateSceneNumber() == 0)
            {
                pathList[i * cameraGroupSize] = opticalFlowCamerasController.initialPath?[i];
                pathList[i * cameraGroupSize + 1] = opticalFlowCamerasController.fovAwarePath?[i];
                pathList[i * cameraGroupSize + 2] = opticalFlowCamerasController.smoothPath?[i];
            }
            else if (Manager.GetActivateSceneNumber() == 1)
            {
                pathList[i] = opticalFlowCamerasController.smoothPath?[i];
            }
            else if (ifBakeWindowAtLast)
            {
                pathList[i] = opticalFlowCamerasController.smoothPath?[i];
            }
        }
        int totalCamNum = cameraList.Length + (ifBakeWindowAtLast ? 1 : 0);
        var camerasAngles = new List<Vector2>[totalCamNum];
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        var originalSize = new Vector2(videoWidth, videoHeight);
        for (int i = 0; i < cameraList.Length; i++)
        {
            camerasAngles[i] = new List<Vector2>();
            if (pathList[i] == null)
                continue;
            foreach (var p in pathList[i])
            {
                var op = opticalFlowCamerasController.PixelToOriginPixelFloatValue(p, downsampleSize, originalSize);
                var a = PixelToAngle(op);
                camerasAngles[i].Add(a);
            }
        }
        Camera[] newCameraList = null;
        if (ifBakeWindowAtLast)
        {
            camerasAngles[totalCamNum - 1] = new List<Vector2>();
            foreach (var a in userMainCamAngles)
                camerasAngles[totalCamNum - 1].Add(a);
            newCameraList = new Camera[cameraList.Length + 1];
            for (int i = 0; i < cameraList.Length; i++)
                newCameraList[i] = cameraList[i];
            newCameraList[newCameraList.Length - 1] = spareCam;
        }

        int[] j = new int[totalCamNum];
        for (int i = 0; i < totalCamNum; i++)
            j[i] = 0;
        var totalFrame = ifBakeWindowAtLast ? userMainCamAngles.Count : (int)videoPlayer.frameCount;
        for (int frame = 0; frame < totalFrame; frame++)
        {
            var t = new Texture2D(textureWidth, textureHeight);
            for (int u = 0; u < textureWidth; u++)
                for (int v = 0; v < textureHeight; v++)
                    t.SetPixel(u, textureHeight - 1 - v, Color.clear);
            if (ifDrawCameraWindow)
            {
                if (!ifBakeWindowAtLast) {
                    DrawCameraWindowToTexture2D(cameraList, camerasAngles, frame, textureWidth, textureHeight, t, ref j, null);                    
                }                    
                else
                {
                    var newColorList = new Color[camerasAngles.Length];
                    for (int i = 0; i < camerasAngles.Length - 1; i++)
                        newColorList[i] = Color.yellow;
                    newColorList[camerasAngles.Length - 1] = Color.red;
                    Debug.Log("*************************");
                    Debug.Log(string.Format("newCameraList.Count:{0}, newColorList.count:{1}, camerasAngles.count:{2}, newCameraList[-1].name:{3}",
                        newCameraList.Length, newColorList.Length, camerasAngles.Length, newCameraList[newCameraList.Length-1].name));
                    DrawCameraWindowToTexture2D(newCameraList, camerasAngles, frame, textureWidth, textureHeight, t, ref j, newColorList);
                }
            }
            t.Apply();
            cameraWindowTextureList.Add(t);
        }
        var timeEnd = DateTime.Now.Ticks;
        var timeCost = (timeEnd - timeStart) / 1e7f;
        Debug.Log("PrepareCameraOpticalFlowWindowTextures Time Cost: " + timeCost);
        opticalFlowCamerasController.renderCamWindowTime = timeCost;
    }

    public Vector3 AngleToEulerAngle(Vector2 a)
    {
        return new Vector3(-a.y, a.x - 90, 0);
    }
    public Vector2 EulerAngleToPixel(Vector3 ea)
    {
        return AngleToPixel(EulerAngleToAngle(ea));
    }
    public Vector2 EulerAngleToAngle(Vector3 ea)
    {
        var x = ea.y + 90;
        var y = -ea.x + 360;
        if (y >= 360)
            y -= 360;
        if (90 < y && y <= 270)
        {
            x += 180;
            y = 180 - y;
        }
        else if (270 < y && y <= 360)
        {
            y -= 360;
        }
        if (x >= 360)
            x -= 360;
        return new Vector2(x, y);
    }
    public float Sqr(float x)
    {
        return x * x;
    }

    public void UpdatePauseContinue(bool isPlaying)
    {
        var nowTime = DateTime.Now.Ticks / 1e7;
        if (nowTime - buttonPauseContinueUpdateTime < 0.1)
            return;
        buttonPauseContinueUpdateTime = nowTime;
        var text = buttonPauseContinue.GetComponentInChildren<Text>();
        var image = buttonPauseContinue.GetComponent<Image>();
        if (isPlaying)
        {
            if (text != null)
                text.text = "Pause";
            else
                image.sprite = manager.pauseImage;
            videoPlayer.Play();
        }
        else
        {
            var s = videoPlayer.frame == 0 ? "Play" : "Continue";
            if (text != null)
                text.text = s;
            else
                image.sprite = manager.continueImage;
            videoPlayer.Pause();
        }
    }

    public void PauseVideo()
    {
        UpdatePauseContinue(false);
    }
    public void ContinueVideo()
    {
        UpdatePauseContinue(true);
    }
    public void ResetCameraNFOVs()
    {
        foreach (var c in cameraNFOVs)
        {
            if (c == null)
                continue;
            c.transform.eulerAngles = Vector3.zero;
        }
    }
    public IEnumerator StopVideoCoroutine()
    {
        videoPlayer.frame = 0;
        videoPlayer.Pause();
        while (videoPlayer.frame != 0)
        {
            yield return null;
        }
        Debug.Log("StopVideo");
        Debug.Log("videoPlayer.frame: " + videoPlayer.frame);
        ResetCameraNFOVs();
        UpdatePauseContinue(false);
        if (Manager.GetActivateSceneNumber() == 1)
        {
            manager.mainNFOVController.VideoNowFrameChangedByUser();
        }
        else if (Manager.GetActivateSceneNumber() >= 2)
        {
            manager.panoramaVideoController.ChangedManually();
        }
        yield return null;
    }
    public void StopVideo()
    {
        StartCoroutine(StopVideoCoroutine());
    }
    private void UpdateTextureMaxLength()
    {
        cameraWindowTextureMaxLength = videoPlayer.targetTexture.width / TextureMaxLengthScale;
        if (Manager.GetActivateSceneNumber() == 1)
        {
            manager.mainNFOVController.locatorTextureMaxLength = videoPlayer.targetTexture.width / TextureMaxLengthScale;
        }
    }
    public IEnumerator PrepareVideo()
    {
        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared)
        {
            yield return null;
        }
        Debug.Log("frameCount: " + videoPlayer.frameCount);
        videoPlayer.targetTexture = new RenderTexture(videoPlayer.texture.width, videoPlayer.texture.height, 0);
        manager.VideoTextureChanges();
        UpdateTextureMaxLength();
        if (Manager.GetActivateSceneNumber() == 1)
        {
            manager.mainNFOVController.InitLocator();
        }
        if (ifBakeWindowAtLast)
            userMainCamAngles.Clear();
        ContinueVideo();
        Debug.Log(string.Format("video width:{0}, height:{1}", videoPlayer.targetTexture.width, videoPlayer.targetTexture.height));
    }
    public void LoadVideo(string path)
    {
        StopVideo();
        videoPlayer.url = path;
        videoPlayer.isLooping = false;
        StartCoroutine(PrepareVideo());
    }
    public void ButtonPauseContinueOnClick()
    {
        UpdatePauseContinue(!videoPlayer.isPlaying);
    }
    
    public void ExportFrameOriginalVideo(int frame)
    {
        Debug.Log("export "+frame);
        var picDir = outputPictureDir;
        var videoDir = outputVideoDir;

        var videoPlayer = manager.videoPlayer;
        var targetTexture = videoPlayer.targetTexture;
        int tw = targetTexture.width;
        int th = targetTexture.height;
        var t = new Texture2D(tw, th);
        //Debug.Log(string.Format("frame: {0}", frame));
        var oldRenderTexture = RenderTexture.active;
        RenderTexture.active = targetTexture;
        t.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
        if (cameraWindowTextureList != null && frame < cameraWindowTextureList.Count)
        {
            var tWindow = cameraWindowTextureList[(int)frame];
            int w = tWindow.width;
            int h = tWindow.height;
            int wSeg = tw / w;
            int hSeg = th / h;
            //Debug.Log(string.Format("wSeg:{0}, hSeg:{1}", wSeg, hSeg));
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    var c = tWindow.GetPixel(i, j);
                    if (c.Equals(Color.clear))
                        continue;
                    for (int ii = 0; ii < wSeg; ii++)
                        for (int jj = 0; jj < hSeg; jj++)
                        {
                            t.SetPixel(i * wSeg + ii, j * hSeg + jj, c);
                        }
                }
        }
        t.Apply();
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        //It's disgusting to lose frames. The exported video must have consecutive file names
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        RenderTexture.active = oldRenderTexture;
        Destroy(t);
    }
    public void ExportJpg(Texture2D t, string fileName)
    {
        var bytes = t.EncodeToJPG(100);
        File.WriteAllBytes(fileName, bytes);
    }
    public IEnumerator ExportBakedVideoCoroutine()
    {
        Debug.Log("Start new ExportBakedVideoCoroutine");
        var timeStart = DateTime.Now.Ticks;
        //ifExportSubVideoCoroutineFinished = false;
        var videoPlayer = manager.videoPlayer;
        var totalFrame = (int)videoPlayer.frameCount;

        var dir = @"C:\Users\yaoling1997\Desktop\output";
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        dir += "/" + timeStart;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        outputPictureDir = dir + "/" + outputPictureDirName;
        if (!Directory.Exists(outputPictureDir))
            Directory.CreateDirectory(outputPictureDir);
        outputVideoDir = dir + "/" + outputVideoDirName;
        if (!Directory.Exists(outputVideoDir))
            Directory.CreateDirectory(outputVideoDir);

        ifExportSubVideoFinished = false;
        exportFrame = 0;
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.frameReady += PVCOnNewFrame;
        videoPlayer.frame = 0;
        videoPlayer.Play();

        int recordFrame = (int)videoPlayer.frame;
        float idleTime = 0;
        float maxIdleTime = 5;
        while (!ifExportSubVideoFinished)
        {
            if (recordFrame == (int)videoPlayer.frame)
            {
                idleTime += Time.deltaTime;
                if (idleTime > maxIdleTime)
                {
                    videoPlayer.Play();
                    idleTime = 0;
                }
            }
            else
            {
                recordFrame = (int)videoPlayer.frame;
                idleTime = 0;
            }
            yield return null;
        }
        Debug.Log("PictureFinished!");
        ConvertPicturesIntoMP4(outputPictureDir, outputVideoDir);
        //Debug.Log("Please wait...");
        if (ifBakeWindowAtLast)
            userMainCamAngles.Clear();
        yield return null;
    }
    private void PVCOnNewFrame(VideoPlayer source, long frame)
    {
        source.Pause();
        if (exportFrame <= frame && frame < cameraWindowTextureList.Count)
        {
            ExportFrameOriginalVideo((int)frame);
        }
        exportFrame = (int)frame + 1;
        var videoPlayer = manager.videoPlayer;
        Debug.Log(string.Format("{0},{1}", frame, cameraWindowTextureList.Count));
        if ((int)frame >= cameraWindowTextureList.Count-2)
        {            
            ifExportSubVideoFinished = true;
            videoPlayer.sendFrameReadyEvents = false;
            videoPlayer.frameReady -= PVCOnNewFrame;
        }
        else
            source.Play();
    }
    
    public void ConvertPicturesIntoMP4(string sourceDir, string targetDir)
    {
        Debug.Log("ConvertPicturesIntoMP4 begin!");
        Debug.Log(string.Format("sourceDir: {0}, targetDir: {1}", sourceDir, targetDir));
        var outputVideoClass = new MenuBarController.OutputVideoClass(sourceDir, targetDir);
        outputVideoClass.StartConvertion();
        Debug.Log("Convert video Finished!");
    }

    public void BakeVideo()
    {
        PrepareCameraOpticalFlowWindowTextures();

        StartCoroutine(ExportBakedVideoCoroutine());
    }
    public void ButtonStopOnClick()
    {
        StopVideo();
        if (ifBakeWindowAtLast)
        {
            BakeVideo();
        }
        if (manager.panoramaVideoController != null)
        {
            manager.panoramaVideoController.UpdateTextMethodStatus(0);
        }
    }
    public void UpdateButtonVoice()
    {
        if (buttonVoice != null)
        {
            if (videoPlayer.GetDirectAudioVolume(0) == 0)
            {
                buttonVoice.GetComponent<Image>().sprite = manager.voiceOffImage;
            }
            else
            {
                buttonVoice.GetComponent<Image>().sprite = manager.voiceOnImage;
            }
        }
    }
    public void ButtonVoiceOnClick()
    {
        videoPlayer.SetDirectAudioVolume(0, 1 - videoPlayer.GetDirectAudioVolume(0));
        UpdateButtonVoice();
    }

    public Vector3 EulerAngleToVector3(Vector3 e)
    {
        return Matrix4x4.Rotate(Quaternion.Euler(e)).MultiplyPoint3x4(new Vector3(0, 0, 1));
    }

    public Vector2 PixelToAngle(Vector2 p)
    {
     
        var resolution = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        p.x = Mathf.Clamp(p.x, 0, resolution.x - 1);
        p.y = Mathf.Clamp(p.y, 0, resolution.y - 1);
        var re = new Vector2(p.x / resolution.x * 360, (p.y / resolution.y - 0.5f) * -180);//(0,h/2)对应角度(0.0)
        return re;
    }
    public Vector2 AngleToPixel(Vector2 a)//The azimuth angle and elevation angle are converted into the pixel coordinates of the original video, X right, y down
    {
        var resolution = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        var re = new Vector2(a.x * resolution.x / 360, (0.5f - a.y / 180) * resolution.y);//(0,h/2)对应角度(0.0)
        re.x = Mathf.Clamp(re.x, 0, resolution.x - 1);
        re.y = Mathf.Clamp(re.y, 0, resolution.y - 1);
        return re;
    }
    public Vector3 PixelToVector3(Vector2 p)
    {
        return EulerAngleToVector3(AngleToEulerAngle(PixelToAngle(p)));
    }
    public bool IfInFOV(Vector2 camAngle, Vector2 pointAngle)
    {
        cameraCalculate.transform.eulerAngles = AngleToEulerAngle(camAngle);
        positionObj.transform.eulerAngles = AngleToEulerAngle(pointAngle);
        var v = cameraCalculate.WorldToViewportPoint(positionObj.transform.forward);
        return v.z > 0 && 0 <= v.x && v.x <= 1 && 0 <= v.y && v.y <= 1;
    }

    private int Dcmp(double x)
    {
        var y = x < 0 ? -x : x;
        if (y < 1e-10)
            return 0;
        return x < 0 ? -1 : 1;
    }
    private void OnDestroy()
    {
        Debug.Log("panelVideoController.OnDestroy");
        Manager.DestroyTexture2dList(pixelSaliencyTextureList);
        Manager.DestroyTexture2dList(regionalSaliencyTextureList);
        Manager.DestroyTexture2dList(opticalFlowTextureList);
        Manager.DestroyTexture2dList(everyCameraPixelSaliencyTextureList);
        Manager.DestroyTexture2dList(everyCameraRegionalSaliencyTextureList);
        Manager.DestroyTexture2dList(cameraWindowTextureList);
    }

}
