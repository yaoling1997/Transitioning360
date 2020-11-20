using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Threading;

public class MenuBarController : MonoBehaviour {
    public static int pixelSaliencyWidth;
    public static int pixelSaliencyHeight;
    public static int pixelOpticalFlowWidth;
    public static int pixelOpticalFlowHeight;
    public static int pixelMaskWidth;
    public static int pixelMaskHeight;

    public EventSystem eventSystem;
    public List<GameObject> menuBarButtons;
    public GameObject[] menuBarButtonPanels;
    private Manager manager;
    private PanelVideoController panelVideoController;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private MainNFOVController mainNFOVController;
    private JArray sal,flx,fly,mask;
    private bool ifExportSubVideoFinished;
    private bool ifExportSubVideoCoroutineFinished;
    public bool ifExecuteCommandFinished;
    private int exportFrame;//The current frame of the video is exported. If the frame is lost, the content of the new frame is copied as the lost frame
    private string rootDir;//Root directory of all exported videos
    private string normalizationRootDir;
    private string withoutNormalizationRootDir;
    private string outputPictureDir;
    private string outputPictureDirName;
    private string outputVideoDir;
    private string outputVideoDirName;
    private Dictionary<string, string> subVideoDirDictionary;

    private bool ifLoadVideoDataFinished;

    private void Awake()
    {
        outputPictureDirName = "Picture";
        outputVideoDirName = "Video";
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        panelVideoController = manager.panelVideoController;
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        mainNFOVController = manager.mainNFOVController;

        ifLoadVideoDataFinished = false;
    }
    
    // Use this for initialization
    void Start () {
    }
	
	// Update is called once per frame
	void Update () {
		
	}
    public void CloseAllMenuBarButtonPanels() {
        foreach (var panel in menuBarButtonPanels)
            panel.SetActive(false);
    }
    public void ImportVideoFromPath(string path) {
        if (!File.Exists(path)) {
            Debug.Log("Video file does not exist!");
            return;
        }
        panelVideoController.LoadVideo(path);
        Debug.Log("Load video succeed!");
        var sceneNumber = Manager.GetActivateSceneNumber();
        if (sceneNumber == 2)
        {
            manager.panoramaVideoController.koreaMethod.Clear();
            manager.panoramaVideoController.multiMethod.Clear();
            manager.panoramaVideoController.pipMethod.Clear();
        }
        else if (sceneNumber == 3)
        {
            manager.panoramaVideoController.koreaMethod.Clear();
            manager.panoramaVideoController.multiMethod.Clear();
        }
    }
    public void ButtonImportVideoOnClick() {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetVideoPath(out path))
        {
            ImportVideoFromPath(path);
        }
        CloseAllMenuBarButtonPanels();
    }

    public void ButtonEnterViewerScene() {
        SceneManager.LoadScene("ViewerScene");
    }
    public void ButtonExitOnClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public void ButtonEnterAnnotateVideoScene() {
        SceneManager.LoadScene("AnnotateVideoScene");
    }
    public void ButtonResetOnClick()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    public bool IfMenuBarButtonSelected()
    {
        var s = eventSystem.currentSelectedGameObject;
        return s != null && menuBarButtons.Contains(s);
    }
    public void ButtonGetRectOfPixel()
    {        
        opticalFlowCamerasController.GetRectsOfFOV();        
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonGetRegionalSaliency()
    {
        var ps = opticalFlowCamerasController.pixelSaliency;
        var psw = pixelSaliencyWidth;
        var psh = pixelSaliencyHeight;
        List<float> maxRegionalSaliencyEveryFrame = null;
        opticalFlowCamerasController.PrepareRegionalSaliency();
        panelVideoController.PrepareRegionalSaliencyTextures();
        CloseAllMenuBarButtonPanels();
    }

    public void ButtonGetCameraPath()
    {
        opticalFlowCamerasController.PreparePath();
        CloseAllMenuBarButtonPanels();
    }
    private void BfsDecayMask(Vector2 beginPixel, bool[,]vis, float[,] mk) {
        var q= new Queue<Vector3>();
        vis[(int)beginPixel.x, (int)beginPixel.y] = true;
        q.Enqueue(new Vector3(beginPixel.x, beginPixel.y,0));
        var move = new Vector2[] {
            new Vector2(-1,0),
            new Vector2(0,-1),
            new Vector2(0,1),
            new Vector2(1,0),
        };
        var vInc = new float[] {            
            0,
            opticalFlowCamerasController.maskDecayV,
            -opticalFlowCamerasController.maskDecayV,
            0            
        };
        while (q.Count>0){
            var u = q.Dequeue();
            mk[(int)u.x, (int)u.y] = Mathf.Max(0, mk[(int)u.x, (int)u.y] + u.z);
            for (int i = 0; i < move.Length; i++) {
                float x = u.x + move[i].x;
                float y = u.y + move[i].y;
                if (0 <= x && x < pixelMaskWidth && 0 <= y && y < pixelMaskHeight) {
                    if (vis[(int)x, (int)y])
                        continue;
                    if (mk[(int)x, (int)y] > 0)
                    {
                        vis[(int)x, (int)y] = true;
                        q.Enqueue(new Vector3(x, y, u.z + vInc[i]));
                    }
                }
            }
        }
    }
    public void LoadPixelMask() {
        if (mask == null || mask.Count == 0) {
            Debug.Log("no mask info");
            return;
        }
        opticalFlowCamerasController.pixelMaskList = new List<float[,]>();
        pixelMaskWidth = 0;
        pixelMaskHeight = 0;
        foreach (var i in mask[0])
            pixelMaskHeight++;
        foreach (var i in mask[0][0])
            pixelMaskWidth++;
        Debug.Log(string.Format("pixelMaskWidth, pixelMaskHeight: {0}, {1}", pixelMaskWidth, pixelMaskHeight));        
        Debug.Log("mask.Count: " + mask.Count);
        var maxMask = 255f;
        for (int keyFrame = 0; keyFrame < mask.Count; keyFrame++) {
            var mk = new float[pixelMaskWidth, pixelMaskHeight];
            var vis = new bool[pixelMaskWidth, pixelMaskHeight];
            for (int i = 0; i < pixelMaskWidth; i++)
                for (int j = 0; j < pixelMaskHeight; j++) {
                    mk[i, j] = (float)mask[keyFrame][j][i]/maxMask;
                    vis[i, j] = false;
                }
            if (opticalFlowCamerasController.useMaskDecay)
                for (int j = 0; j < pixelMaskHeight; j++)
                    for (int i = 0; i < pixelMaskWidth; i++)
                        if (!vis[i, j] && mk[i, j] > 0.5)
                            BfsDecayMask(new Vector2(i,j),vis,mk);

            opticalFlowCamerasController.pixelMaskList.Add(mk);
        }
        if (Manager.GetActivateSceneNumber() == 0) {
            panelVideoController.PrepareMaskTextures();
            panelVideoController.PreparePixelBlendSaliencyMaskTextures();
        }            
    }
    public void LoadPixelSaliencyAndOpticalFlow() {
        if (sal == null || sal.Count == 0) {
            Debug.Log("no sal infomation");
            return;
        }        
        opticalFlowCamerasController.pixelSaliency = new List<float[,]>();
        opticalFlowCamerasController.normalizedPixelSaliency = new List<float[,]>();
        opticalFlowCamerasController.pixelOpticalFlow = new List<Vector2[,]>();
        pixelSaliencyWidth = 0;
        pixelSaliencyHeight = 0;
        pixelOpticalFlowWidth = 0;
        pixelOpticalFlowHeight = 0;
        foreach (var i in sal[0])
            pixelSaliencyHeight++;
        foreach (var i in sal[0][0])
            pixelSaliencyWidth++;

        foreach (var i in flx[0])
            pixelOpticalFlowHeight++;
        foreach (var i in flx[0][0])
            pixelOpticalFlowWidth++;

        Debug.Log(string.Format("pixelSaliencyW, pixelSaliencyH: {0}, {1}",pixelSaliencyWidth, pixelSaliencyHeight));
        Debug.Log(string.Format("flxW, flxH: {0}, {1}", pixelOpticalFlowWidth, pixelOpticalFlowHeight));
        Debug.Log("sal.Count: " + sal.Count);
        Debug.Log("flx.Count: " + flx.Count);
        Debug.Log("fly.Count: " + fly.Count);
        float maxTotalSal = 0;
        int maxTotalSalIndex = 0;
        var tmpMaxSalList = new List<float>();
        for (int keyFrame = 0; keyFrame < flx.Count; keyFrame++)
        {
            var ps = new float[pixelSaliencyWidth, pixelSaliencyHeight];
            var nps = new float[pixelSaliencyWidth, pixelSaliencyHeight];
            var of = new Vector2[pixelOpticalFlowWidth, pixelOpticalFlowHeight];
            float maxSal = 0;
            float maxFlxAbs = -1e9f;
            float maxFlyAbs = -1e9f;
            float maxFlowLength = 0f;
            var salKeyFrame = (int)((float)keyFrame * sal.Count / flx.Count);
            for (int i = 0; i < pixelSaliencyWidth; i++)
                for (int j = 0; j < pixelSaliencyHeight; j++)
                {
                    ps[i, j] = (float)sal[salKeyFrame][j][i];
                    maxSal = Mathf.Max(maxSal, ps[i, j]);
                }
            for (int i = 0; i < pixelSaliencyWidth; i++)
                for (int j = 0; j < pixelSaliencyHeight; j++)                
                    nps[i, j] = maxSal > 0 ? ps[i, j] / maxSal : 0;                                    

            for (int i = 0; i < pixelOpticalFlowWidth; i++)
                for (int j = 0; j < pixelOpticalFlowHeight; j++)
                {
                    of[i, j] = new Vector2((float)flx[keyFrame][j][i], (float)fly[keyFrame][j][i]);
                    maxFlxAbs = Mathf.Max(maxFlxAbs, Mathf.Abs(of[i, j].x));
                    maxFlyAbs = Mathf.Max(maxFlyAbs, Mathf.Abs(of[i, j].y));
                    maxFlowLength = Mathf.Max(maxFlowLength, of[i, j].magnitude);
                }
            opticalFlowCamerasController.pixelSaliency.Add(ps);
            opticalFlowCamerasController.normalizedPixelSaliency.Add(nps);            
            opticalFlowCamerasController.pixelOpticalFlow.Add(of);
            tmpMaxSalList.Add(maxSal);
            if (maxSal > maxTotalSal) {
                maxTotalSalIndex = keyFrame;
                maxTotalSal = maxSal;
            }            
        }
        Debug.Log(string.Format("maxTotalSal:{0}, maxTotalSalIndex:{1}", maxTotalSal, maxTotalSalIndex));
        Debug.Log("tmpMaxSalList[maxTotalSalIndex]: " + tmpMaxSalList[maxTotalSalIndex]);
        if (Manager.GetActivateSceneNumber() == 0) {
            panelVideoController.PreparePixelSaliencyTextures();
            panelVideoController.PrepareOpticalFlowTextures();
            panelVideoController.PreparePixelBlendSaliencyMaskTextures();
        }
    }
    public JObject JsonFileToJObject(string path) {
        Debug.Log("json path: " + path);
        var content = File.ReadAllText(path);        
        return JObject.Parse(content);
    }
    public JArray JsonFileToJArray(string path)
    {
        Debug.Log("json path: " + path);
        var content = File.ReadAllText(path);
        return JArray.Parse(content);
    }
    //Read JSON data from path
    public void LoadPSAndOFFromPath(string path) {
        var timeStart = DateTime.Now.Ticks;        
        var tmp = path.Split('/', '\\');
        var dirName = tmp[tmp.Length - 1];
        var sal_path = path + "/" + dirName + "_sal.json";
        var flx_path = path + "/" + dirName + "_flx.json";
        var fly_path = path + "/" + dirName + "_fly.json";
        try
        {
            sal = JsonFileToJArray(sal_path);
            flx = JsonFileToJArray(flx_path);
            fly = JsonFileToJArray(fly_path);
        }
        catch
        {
            sal = JArray.Parse(JsonFileToJObject(sal_path)["sal"].ToString());
            flx = JArray.Parse(JsonFileToJObject(flx_path)["flx"].ToString());
            fly = JArray.Parse(JsonFileToJObject(fly_path)["fly"].ToString());
        }
        LoadPixelSaliencyAndOpticalFlow();
        var timeEnd = DateTime.Now.Ticks;
        sal.Clear();
        flx.Clear();
        fly.Clear();
        Debug.Log("Load PS and OF timeCost: " + (timeEnd - timeStart) / 1e7);
    }
    //From the folder load pixel salience and optical flow, the folder name is the same as the video name
    public void ButtonLoadPSAndOFFromDir()
    {
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "ChooseDir",
            ShowNewFolderButton = false   
        };                
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {            
            path = fb.SelectedPath;
            Debug.Log("path: " + path);
            LoadPSAndOFFromPath(path);
        }
        else
        {
            Debug.Log("cancel!");
            return;
        }
        fb.Dispose();
        CloseAllMenuBarButtonPanels();
    }
    //load pixel saliency and optical flow 
    public void ButtonLoadPSAndOF()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetPSAndOFFilePath(out path))
        {
            var timeStart = DateTime.Now.Ticks;            
            JObject data = JsonFileToJObject(path);
            sal = JArray.Parse(data["sal"].ToString());
            flx = JArray.Parse(data["flx"].ToString());
            fly = JArray.Parse(data["fly"].ToString());
            LoadPixelSaliencyAndOpticalFlow();
            var timeEnd = DateTime.Now.Ticks;
            Debug.Log("Load PS and OF timeCost: " + (timeEnd - timeStart) / 1e7);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void LoadMaskFromPath(string path) {
        if (!File.Exists(path)) {
            Debug.Log("Mask file does not exist!");
            opticalFlowCamerasController.pixelMaskList = null;
            return;
        }
        JObject data = JsonFileToJObject(path);
        mask = JArray.Parse(data["mask"].ToString());
        LoadPixelMask();
    }
    //load human mask
    public void ButtonLoadMask()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetPSAndOFFilePath(out path))
        {
            var timeStart = DateTime.Now.Ticks;
            LoadMaskFromPath(path);
            var timeEnd = DateTime.Now.Ticks;
            Debug.Log("Load Mask timeCost: " + (timeEnd - timeStart) / 1e7);
        }
        CloseAllMenuBarButtonPanels();
    }
    //Load rectofpixel from path
    public void LoadRectOfPixelFromPath(string path) {
        if (!File.Exists(path))
        {
            Debug.Log("RectOfPixel file does not exist!");
            return;
        }
        var content = File.ReadAllText(path);
        var recordRectOfPixel = RecordRectOfPixel.CreateFromJSON(content);
        opticalFlowCamerasController.rectOfPixel = recordRectOfPixel.GetRectOfPixel();
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var ps = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, false);        
        opticalFlowCamerasController.PrepareRegionalSaliency();        

        if (Manager.GetActivateSceneNumber() == 0)
            panelVideoController.PrepareRegionalSaliencyTextures();
        
    }
    public void ButtonLoadRectOfPixel()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetLoadRectOfPixelFilePath(out path))
        {
            LoadRectOfPixelFromPath(path);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonLoadRegionalSaliencyPaths()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetLoadRegionalSaliencyFilePath(out path))
        {
            var content = File.ReadAllText(path);
            var recordRegionalSaliency = RecordRegionalSaliency.CreateFromJSON(content);
            opticalFlowCamerasController.GetRectsOfFOV();
            opticalFlowCamerasController.regionalSaliency = recordRegionalSaliency.regionalSaliency;
            panelVideoController.PrepareRegionalSaliencyTextures();
        }
        CloseAllMenuBarButtonPanels();
    }
    public void LoadCameraPathsFromFile(string path) {
        if (!File.Exists(path)) {
            Debug.Log("CameraPaths file does not exist!");
            return;
        }
        opticalFlowCamerasController.LoadCameraPaths3(path);
    }
    public void ButtonLoadCameraPaths()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetLoadCameraPathsFilePath(out path))
        {
            LoadCameraPathsFromFile(path);
        }
        CloseAllMenuBarButtonPanels();

    }
    public IEnumerator LoadOtherData(string path) {
        var tmp = path.Split('/', '\\');
        var dirName = tmp[tmp.Length - 1];
        var maskPath = path + "/" + dirName + "_mask.json";
        var rectOfPixelPath = path + "/" + "rectOfPixel_180_101.json";
        var cameraPath = path + "/" + "cp3.json";
        while (!manager.videoPlayer.isPrepared)
            yield return null;
        LoadPSAndOFFromPath(path);
        LoadMaskFromPath(maskPath);
        LoadRectOfPixelFromPath(rectOfPixelPath);
        LoadCameraPathsFromFile(cameraPath);
        ifLoadVideoDataFinished = true;
    }
    public void LoadDataByOneClick(string path) {
        var tmp = path.Split('/', '\\');
        var dirName = tmp[tmp.Length - 1];
        var videoPath = path + "/" + dirName + ".mp4";

        //load video
        ImportVideoFromPath(videoPath);
        //load saliency and opticalFlow
        ifLoadVideoDataFinished = false;
        StartCoroutine(LoadOtherData(path));
    }
    public void ButtonLoadDataByOneClick()
    {
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "ChooseDir",
            ShowNewFolderButton = false   
        };        
        
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var timeStart = DateTime.Now.Ticks;
            path = fb.SelectedPath;
            Debug.Log("path: " + path);

            LoadDataByOneClick(path);

            var timeEnd = DateTime.Now.Ticks;
            Debug.Log("LoadDataByOneClick timeCost: " + (timeEnd - timeStart) / 1e7);
        }
        else
        {
            Debug.Log("cancel!");
            return;
        }
        fb.Dispose();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonSaveRectOfPixel()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetSaveRectOfPixelFilePath(out path))
        {
            var rectOfPixel = new RecordRectOfPixel();
            int downsampleWidth, downsampleHeight;
            opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
            var a = downsampleWidth;
            var b = downsampleHeight;
            var c = opticalFlowCamerasController.rectOfPixel;
            rectOfPixel.Set(a, b, c);
            var content = rectOfPixel.SaveToString();
            File.WriteAllText(path, content);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonSaveRegionalSaliency()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetSaveRegionalSaliencyFilePath(out path))
        {            
            var recordRegionalSaliency = new RecordRegionalSaliency();
            int downsampleWidth, downsampleHeight;
            opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
            recordRegionalSaliency.downsampleWidth = downsampleWidth;
            recordRegionalSaliency.downsampleHeight = downsampleHeight;
            recordRegionalSaliency.regionalSaliency = opticalFlowCamerasController.regionalSaliency;
            var content = recordRegionalSaliency.SaveToString();
            File.WriteAllText(path, content);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonSaveCameraPaths()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetSaveCameraPathsFilePath(out path))
        {
            opticalFlowCamerasController.SaveCameraPaths(path);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void ExportJpg(Texture2D t,string fileName) {
        var bytes = t.EncodeToJPG(100);
        File.WriteAllBytes(fileName, bytes);
    }
    
    public void ExportFrameOnlyWindow(int frame)
    {
        var picDir = outputPictureDir + "/" + "onlyWindow";
        var videoDir = outputVideoDir + "/" + "onlyWindow";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);
        var videoPlayer = manager.videoPlayer;
        var targetTexture = videoPlayer.targetTexture;
        int tw = targetTexture.width;
        int th = targetTexture.height;
        var t = new Texture2D(tw, th);
        for (int i = 0; i < tw; i++)
            for (int j = 0; j < th; j++)
                t.SetPixel(i, j, Color.white);
        var cameraWindowTextureList = panelVideoController.cameraWindowTextureList;
        if (cameraWindowTextureList != null && frame < cameraWindowTextureList.Count)
        {
            var tWindow = cameraWindowTextureList[(int)frame];
            int w = tWindow.width;
            int h = tWindow.height;
            int wSeg = tw / w;
            int hSeg = th / h;
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
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        Destroy(t);
    }
    public void ExportFrameOriginalVideoWithWindow(int frame) {
        var picDir = outputPictureDir + "/" + "originVideoWithWindow";
        var videoDir = outputVideoDir + "/" + "originVideoWithWindow";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);
        var videoPlayer = manager.videoPlayer;
        var targetTexture = videoPlayer.targetTexture;
        int tw = targetTexture.width;
        int th = targetTexture.height;
        var t = new Texture2D(tw, th);
        var oldRenderTexture = RenderTexture.active;
        RenderTexture.active = targetTexture;
        t.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
        var cameraWindowTextureList = panelVideoController.cameraWindowTextureList;
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
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++) {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        RenderTexture.active = oldRenderTexture;
        Destroy(t);
    }
    public void ExportFrameOriginalVideo(int frame)
    {
        var picDir = outputPictureDir + "/" + "originVideo";
        var videoDir = outputVideoDir + "/" + "originVideo";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);
        var videoPlayer = manager.videoPlayer;
        var targetTexture = videoPlayer.targetTexture;
        int tw = targetTexture.width;
        int th = targetTexture.height;
        var t = new Texture2D(tw, th);
        //Debug.Log(string.Format("frame: {0}", frame));
        var oldRenderTexture = RenderTexture.active;
        RenderTexture.active = targetTexture;
        t.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
        t.Apply();
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        RenderTexture.active = oldRenderTexture;
        Destroy(t);
    }
    public void ExportFrameSaliency(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var pixelSaliencyTextureList = panelVideoController.pixelSaliencyTextureList;
        if (pixelSaliencyTextureList == null || pixelSaliencyTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "saliency";
        var videoDir = outputVideoDir + "/" + "saliency";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * pixelSaliencyTextureList.Count);
        //Debug.Log(string.Format("son of a bitch, keyFrame:{0}, pixelSaliencyTextureList.count:{1}", keyFrame, pixelSaliencyTextureList.Count));
        //Debug.Log(string.Format("son of a bitch, videoPlayer.frame:{0},videoPlayer.frameCount:{1}", videoPlayer.frame, videoPlayer.frameCount));
        var t = pixelSaliencyTextureList[keyFrame];
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
    }
    public void ExportFrameReginalSaliency(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var regionalSaliencyTextureList = panelVideoController.regionalSaliencyTextureList;
        if (regionalSaliencyTextureList == null || regionalSaliencyTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "regionalSaliency";
        var videoDir = outputVideoDir + "/" + "regionalSaliency";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * regionalSaliencyTextureList.Count);
        var t = EnlargeTexture2D(regionalSaliencyTextureList[keyFrame]);
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        Destroy(t);
    }
    public void ExportFrameOpticalFlow(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var opticalFlowTextureList = panelVideoController.opticalFlowTextureList;
        if (opticalFlowTextureList == null || opticalFlowTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "opticalFlow";
        var videoDir = outputVideoDir + "/" + "opticalFlow";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * opticalFlowTextureList.Count);
        var t = opticalFlowTextureList[keyFrame];
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
    }
    public void ExportFrameMask(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var pixelMaskTextureList = panelVideoController.pixelMaskTextureList;
        if (pixelMaskTextureList == null || pixelMaskTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "mask";
        var videoDir = outputVideoDir + "/" + "mask";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * pixelMaskTextureList.Count);
        var t = pixelMaskTextureList[keyFrame];
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
    }
    public void ExportFrameMaskBlendOriginalVideo(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var pixelMaskList = opticalFlowCamerasController.pixelMaskList;
        if (pixelMaskList == null || pixelMaskList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "maskBlendOriginalVideo";
        var videoDir = outputVideoDir + "/" + "maskBlendOriginalVideo";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * pixelMaskList.Count);
        var pixelMask = pixelMaskList[keyFrame];
        
        var targetTexture = videoPlayer.targetTexture;
        int tw = targetTexture.width;
        int th = targetTexture.height;
        var t = new Texture2D(tw, th);
        //Debug.Log(string.Format("frame: {0}", frame));
        var oldRenderTexture = RenderTexture.active;
        RenderTexture.active = targetTexture;
        t.ReadPixels(new Rect(0, 0, tw, th), 0, 0);

        int w = pixelMaskWidth;
        int h = pixelMaskHeight;
        float wSeg = (float)tw / w;
        float hSeg = (float)th / h;
        //Debug.Log(string.Format("wSeg:{0}, hSeg:{1}", wSeg, hSeg));
        
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                var cy = Color.green;
                var cMask = new Color(pixelMask[i, j]* cy.r, pixelMask[i, j] * cy.g, pixelMask[i, j] * cy.b);
                if (cMask.Equals(Color.clear))
                    continue;
                for (int ii = (int)(i * wSeg); ii < (int)((i + 1) * wSeg); ii++)
                    for (int jj = (int)(j * hSeg); jj < (int)((j + 1) * hSeg); jj++)
                    {
                        if (ii >= tw || jj >= th)
                            continue;
                        var ct = t.GetPixel(ii, th - jj - 1);
                        //var cTmp = new Color((cMask.r + ct.r)/2, (cMask.g + ct.g)/2, (cMask.b + ct.b)/2, 1);
                        //var cTmp = new Color(cMask.r * 0.5f + ct.r, ct.g, ct.b, 1);
                        var cTmp = new Color(cMask.r * 0.5f + ct.r, cMask.g * 0.5f + ct.g, cMask.b * 0.5f + ct.b, 1);
                        t.SetPixel(ii, th - jj - 1, cTmp);
                    }
            }        
        t.Apply();
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        RenderTexture.active = oldRenderTexture;
        Destroy(t);
    }
    public void ExportFrameBlend(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var pixelBlendSaliencyMaskTextureList = panelVideoController.pixelBlendSaliencyMaskTextureList;
        if (pixelBlendSaliencyMaskTextureList == null || pixelBlendSaliencyMaskTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "blend";
        var videoDir = outputVideoDir + "/" + "blend";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * pixelBlendSaliencyMaskTextureList.Count);
        var t = pixelBlendSaliencyMaskTextureList[keyFrame];
        //Debug.Log(string.Format("frame: {0}, videoPlayer.frame: {1}", frame, videoPlayer.frame));
        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
    }
    public Texture2D EnlargeTexture2D(Texture2D oldTex) {
        int w = pixelSaliencyWidth;
        int h = pixelSaliencyHeight;
        int ow = oldTex.width;
        int oh = oldTex.height;
        int wSeg = w / ow;
        int hSeg = h / oh;
        var t = new Texture2D(w, h);
        if (panelVideoController.ifTextureFilterModePoint)
            t.filterMode = FilterMode.Point;
        for (int i = 0; i < ow; i++)
            for (int j = 0; j < oh; j++) {
                var c = oldTex.GetPixel(i, j);
                for (int ii = 0; ii < wSeg; ii++)
                    for (int jj = 0; jj < hSeg; jj++) {
                        t.SetPixel(i * wSeg + ii, j * hSeg + jj, c);
                    }
            }
        t.Apply();
        return t;
    }
    public void ExportFrameMethod3Saliency(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var method3PixelSaliencyTextureList = opticalFlowCamerasController.method3PixelSaliencyTextureList;
        if (method3PixelSaliencyTextureList == null || method3PixelSaliencyTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "method3Saliency";
        var videoDir = outputVideoDir + "/" + "method3Saliency";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * method3PixelSaliencyTextureList.Count);
        var t = EnlargeTexture2D(method3PixelSaliencyTextureList[keyFrame]);

        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        Destroy(t);
    }
    public void ExportFrameMethod3OpticalFlow(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var method3OpticalFlowTextureList = opticalFlowCamerasController.method3OpticalFlowTextureList;
        if (method3OpticalFlowTextureList == null || method3OpticalFlowTextureList.Count == 0)
            return;
        var picDir = outputPictureDir + "/" + "method3OpticalFlow";
        var videoDir = outputVideoDir + "/" + "method3OpticalFlow";
        if (!subVideoDirDictionary.ContainsKey(picDir))
            subVideoDirDictionary.Add(picDir, videoDir);

        if (!Directory.Exists(picDir))
            Directory.CreateDirectory(picDir);
        if (!Directory.Exists(videoDir))
            Directory.CreateDirectory(videoDir);

        //Debug.Log(string.Format("frame: {0}", frame));
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * method3OpticalFlowTextureList.Count);
        var t = EnlargeTexture2D(method3OpticalFlowTextureList[keyFrame]);

        for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
        {
            var fileName = picDir + "/" + interFrame + ".jpg";
            ExportJpg(t, fileName);
        }
        Destroy(t);
    }
    public void ExportSubCamVideo(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var cameraNFOV = panelVideoController.cameraNFOVs;
        if (cameraNFOV == null || cameraNFOV.Length == 0)
            return;
        if (opticalFlowCamerasController.smoothPath == null || opticalFlowCamerasController.smoothPath.Length == 0)
            return;
        var t = new Texture2D(cameraNFOV[0].targetTexture.width, cameraNFOV[0].targetTexture.height);
        opticalFlowCamerasController.UpdateCameras();
        int camGroupNum = panelVideoController.cameraGroupNum;
        int camGroupSize = panelVideoController.cameraGroupSize;
        var camSubDirName = new string[3] { "InitialPath", "refineInitialPathOrFovAwarePath", "smoothPath" };
        for (int camGroupId = 0; camGroupId < camGroupNum; camGroupId++)
        {
            var camPicDir = outputPictureDir + "/cam_" + camGroupId;
            var camVideoDir = outputVideoDir + "/cam_" + camGroupId;
            if (!Directory.Exists(camPicDir))
                Directory.CreateDirectory(camPicDir);
            if (!Directory.Exists(camVideoDir))
                Directory.CreateDirectory(camVideoDir);
            for (int i = 0; i < 3; i++)
            {
                var camSubPicDir = camPicDir + "/" + camSubDirName[i];
                var camSubVideoDir = camVideoDir + "/" + camSubDirName[i];
                if (!Directory.Exists(camSubPicDir))
                    Directory.CreateDirectory(camSubPicDir);
                if (!Directory.Exists(camSubVideoDir))
                    Directory.CreateDirectory(camSubVideoDir);
                if (!subVideoDirDictionary.ContainsKey(camSubPicDir))
                    subVideoDirDictionary.Add(camSubPicDir, camSubVideoDir);
                var camId = camGroupId * camGroupSize + i;
                cameraNFOV[camId].Render();
                RenderTexture.active = cameraNFOV[camId].targetTexture;
                t.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                t.Apply();
                for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
                {
                    var fileName = camSubPicDir + "/" + interFrame + ".jpg";
                    ExportJpg(t, fileName);
                }
            }            
        }
        RenderTexture.active = null;
        Destroy(t);
    }
    public void ExportEveryCameraDownsamplePixelSaliency(int frame)
    {
        var videoPlayer = manager.videoPlayer;
        var cameraNFOV = panelVideoController.cameraNFOVs;        
        var everyCameraPixelSaliencyTextureList = panelVideoController.everyCameraPixelSaliencyTextureList;
        if (cameraNFOV == null || cameraNFOV.Length == 0)
            return;        
        if (everyCameraPixelSaliencyTextureList == null || everyCameraPixelSaliencyTextureList.Count == 0)
            return;
        var totalFrame = everyCameraPixelSaliencyTextureList[0].Count;
        if (totalFrame == 0)
            return;
        var keyFrame = (int)((float)frame / videoPlayer.frameCount * totalFrame);        
        int camGroupNum = panelVideoController.cameraGroupNum;
        for (int camGroupId = 0; camGroupId < camGroupNum; camGroupId++)
        {
            var camPicDir = outputPictureDir + "/cam_" + camGroupId;
            var camVideoDir = outputVideoDir + "/cam_" + camGroupId;
            if (!Directory.Exists(camPicDir))
                Directory.CreateDirectory(camPicDir);
            if (!Directory.Exists(camVideoDir))
                Directory.CreateDirectory(camVideoDir);

            var camSubPicDir = camPicDir + "/" + "greedySalieny";
            var camSubVideoDir = camVideoDir + "/" + "greedySalieny";
            if (!Directory.Exists(camSubPicDir))
                Directory.CreateDirectory(camSubPicDir);
            if (!Directory.Exists(camSubVideoDir))
                Directory.CreateDirectory(camSubVideoDir);
            if (!subVideoDirDictionary.ContainsKey(camSubPicDir))
                subVideoDirDictionary.Add(camSubPicDir, camSubVideoDir);

            var t = EnlargeTexture2D(everyCameraPixelSaliencyTextureList[camGroupId][keyFrame]);
            for (var interFrame = exportFrame; interFrame <= frame; interFrame++)
            {
                var fileName = camSubPicDir + "/" + interFrame + ".jpg";
                ExportJpg(t, fileName);
            }
            Destroy(t);
        }                
    }

    private void OnNewFrame(VideoPlayer source, long frame)
    {
        source.Pause();
        if (exportFrame <= frame && frame < (int)source.frameCount)
        {
            ExportFrameOriginalVideoWithWindow((int)frame);
            ExportFrameSaliency((int)frame);
            ExportFrameReginalSaliency((int)frame);
            ExportFrameOpticalFlow((int)frame);
            ExportFrameMask((int)frame);
            ExportFrameBlend((int)frame);
            ExportFrameMethod3Saliency((int)frame);
            ExportFrameMethod3OpticalFlow((int)frame);
            ExportSubCamVideo((int)frame);
            ExportEveryCameraDownsamplePixelSaliency((int)frame);

            //extra
            //ExportFrameOriginalVideo((int)frame);
            ExportFrameMaskBlendOriginalVideo((int)frame);
            //ExportFrameOnlyWindow((int)frame);
        }
        exportFrame = (int)frame + 1;//期待的下一个帧
        var videoPlayer = manager.videoPlayer;
        if ((int)frame >= (int)source.frameCount - 1)
        {
            ifExportSubVideoFinished = true;
            videoPlayer.sendFrameReadyEvents = false;
            videoPlayer.frameReady -= OnNewFrame;
        }
        else
            source.Play();
    }
    public void ButtonUpdateUIWithoutWindowOnClick() {        
        panelVideoController.PreparePixelSaliencyTextures();
        opticalFlowCamerasController.PrepareRegionalSaliency();
        panelVideoController.PrepareRegionalSaliencyTextures();
        panelVideoController.PrepareOpticalFlowTextures();
        panelVideoController.PreparePixelBlendSaliencyMaskTextures();
        panelVideoController.PrepareMaskTextures();
        opticalFlowCamerasController.UpdateEveryCameraPixelSaliencyTextureList();
        opticalFlowCamerasController.UpdateMethod3PixelSaliencyAndOpticalFlowTextureList();
        //panelVideoController.PrepareEveryCameraPixelSaliencyTextures();
        panelVideoController.PrepareEveryCameraRegionalSaliencyTextures();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonUpdateCamWindow() {
        panelVideoController.PrepareCameraOpticalFlowWindowTextures();
        CloseAllMenuBarButtonPanels();
    }
    public IEnumerator ExportSubVideoCoroutine() {
        Debug.Log("Start new ExportSubVideoCoroutine");
        var timeStart = DateTime.Now.Ticks;
        ifExportSubVideoCoroutineFinished = false;
        var videoPlayer = manager.videoPlayer;
        var totalFrame = (int)videoPlayer.frameCount;

        opticalFlowCamerasController.ifUseNormalizationPixelSaliency = false;
        ButtonUpdateUIWithoutWindowOnClick();

        outputPictureDir = withoutNormalizationRootDir + "/" + outputPictureDirName;
        if (!Directory.Exists(outputPictureDir))
            Directory.CreateDirectory(outputPictureDir);
        outputVideoDir = withoutNormalizationRootDir + "/" + outputVideoDirName;
        if (!Directory.Exists(outputVideoDir))
            Directory.CreateDirectory(outputVideoDir);
        subVideoDirDictionary = new Dictionary<string, string>();

        //var targetTexture = videoPlayer.texture as RenderTexture;
        ifExportSubVideoFinished = false;
        exportFrame = 0;
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.frameReady += OnNewFrame;
        videoPlayer.frame = 0;
        videoPlayer.Play();

        int recordFrame = (int)videoPlayer.frame;
        float idleTime = 0;
        float maxIdleTime = 5;
        while (!ifExportSubVideoFinished) {
            if (recordFrame == (int)videoPlayer.frame)
            {
                idleTime += Time.deltaTime;
                if (idleTime > maxIdleTime) {
                    videoPlayer.Play();
                    idleTime = 0;
                }
            }
            else {
                recordFrame = (int)videoPlayer.frame;
                idleTime = 0;
            }
            yield return null;
        }            
        ConvertPicturesIntoMP4();
        Debug.Log("Please wait...");
        yield return null;
        //////////////////////////////////
        opticalFlowCamerasController.ifUseNormalizationPixelSaliency = true;
        ButtonUpdateUIWithoutWindowOnClick();

        outputPictureDir = normalizationRootDir + "/" + outputPictureDirName;
        if (!Directory.Exists(outputPictureDir))
            Directory.CreateDirectory(outputPictureDir);
        outputVideoDir = normalizationRootDir + "/" + outputVideoDirName;
        if (!Directory.Exists(outputVideoDir))
            Directory.CreateDirectory(outputVideoDir);
        subVideoDirDictionary = new Dictionary<string, string>();

        //var targetTexture = videoPlayer.texture as RenderTexture;
        ifExportSubVideoFinished = false;
        exportFrame = 0;
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.frameReady += OnNewFrame;
        videoPlayer.frame = 0;
        videoPlayer.Play();

        recordFrame = (int)videoPlayer.frame;
        idleTime = 0;
        while (!ifExportSubVideoFinished) {
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

        ConvertPicturesIntoMP4();

        opticalFlowCamerasController.ifUseNormalizationPixelSaliency = false;

        var timeEnd = DateTime.Now.Ticks;
        Debug.Log("ExportSubVideoCoroutineFinished, timeCost: "+ (timeEnd - timeStart) / 1e7);
        ifExportSubVideoCoroutineFinished = true;        
    }
    public class OutputVideoClass {
        private string sourceDir;
        private string targetDir;        
        public OutputVideoClass(string sd,string td) {
            sourceDir = sd;
            targetDir = td;
        }
        public void StartConvertion() {
            var fileName = Application.dataPath + "/Plugins/ffmpeg.exe";
            var sourceFile = string.Format("\"{0}/%d.jpg\"", sourceDir);
            var targetFile = string.Format("\"{0}/output.mp4\"", targetDir);
            var args = string.Format("-f image2 -i {0} {1}", sourceFile, targetFile);
            Process p = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo(fileName, args);
            p.StartInfo.UseShellExecute = false;   
            p.StartInfo.RedirectStandardInput = true;   
            p.StartInfo.RedirectStandardOutput = true;   
            p.StartInfo.RedirectStandardError = true;    
            p.StartInfo.CreateNoWindow = true;        
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo = startInfo;
            p.Start();
        }
    }
    public void ConvertPicturesIntoMP4() {        
        foreach (var keyValuePair in subVideoDirDictionary) {
            var sourceDir = keyValuePair.Key;
            var targetDir = keyValuePair.Value;
            Debug.Log(string.Format("sourceDir: {0}, targetDir: {1}", sourceDir, targetDir));
            var outputVideoClass = new OutputVideoClass(sourceDir, targetDir);            
            outputVideoClass.StartConvertion();
        }
        Debug.Log("Convert video Finished!");
    }
    public void ExportSubVideoToDirectory() {
        if (!Directory.Exists(rootDir))
            Directory.CreateDirectory(rootDir);
        normalizationRootDir= rootDir + "/" + "normalization";
        if (!Directory.Exists(normalizationRootDir))
            Directory.CreateDirectory(normalizationRootDir);
        withoutNormalizationRootDir = rootDir + "/" + "withoutNormalization";
        if (!Directory.Exists(withoutNormalizationRootDir))
            Directory.CreateDirectory(withoutNormalizationRootDir);
        StartCoroutine(ExportSubVideoCoroutine());        
    }
    public void ButtonExportSubVideos()
    {
        panelVideoController.PauseVideo();
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose Export Dir",
            ShowNewFolderButton = false   
        };        
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            path = fb.SelectedPath;
            rootDir = path;
            ExportSubVideoToDirectory();
        }
        else
        {
            Debug.Log("cancel export!");
            return;
        }
        fb.Dispose();
        CloseAllMenuBarButtonPanels();
    }    
    public IEnumerator ExecuteCommandFromFile(string path,string outputRootDir = "C:/Users/yaoling1997/Desktop/output") {
        ifExecuteCommandFinished = false;
        while (!ifLoadVideoDataFinished)
            yield return null;
        var timeStart = DateTime.Now.Ticks;
        var commandLines = File.ReadLines(path);
        //The command format is x = y
        foreach (var command in commandLines) {
            Debug.Log("comand: " + command);
            if (command.Length == 0)
                continue;
            var split = command.Split(' ');
            if (split.Length != 3)
            {
                Debug.LogError("error command: " + command);
            }
            else {
                var x = split[0].ToLower();
                var y = split[2];
                var floatY= GeneralManager.StringToFloat(y);
                var intY = Mathf.RoundToInt(floatY);
                switch (x) {
                    case "texturemaxlengthscale":
                        panelVideoController.TextureMaxLengthScale = intY;
                        break;
                    case "cam":
                        int newCamNum = intY;
                        if (newCamNum < 1 || newCamNum > 4)
                        {
                            Debug.LogError("camNum out of range!");
                        }
                        else {
                            panelVideoController.cameraGroupNum = newCamNum;
                        }
                        break;
                    case "w0":
                        opticalFlowCamerasController.w0 = floatY;
                        break;
                    case "overlapweight":
                        opticalFlowCamerasController.overlapWeight = floatY;
                        break;
                    case "blendsaliencyweight":
                        opticalFlowCamerasController.blendSaliencyWeight = floatY;
                        break;
                    case "blendmaskweight":
                        opticalFlowCamerasController.blendMaskWeight = floatY;
                        break;
                    case "usefovpath":
                        y = y.ToLower();
                        if (y.Equals("true"))
                        {
                            opticalFlowCamerasController.ifUseFovPath = true;
                        }
                        else if (y.Equals("false"))
                        {
                            opticalFlowCamerasController.ifUseFovPath = false;
                        }
                        else {
                            Debug.LogError("error usefovpath param");
                        }                        
                        break;
                    case "dpsalstrategy":
                        y = y.ToLower();
                        if (y.Equals("usemaxblendmethod3"))
                        {
                            opticalFlowCamerasController.ifUseMaxBlendMethod3 = true;
                        }
                        else if (y.Equals("usetopkopticalflowmaxsalmethod3"))
                        {
                            opticalFlowCamerasController.ifUseMaxBlendMethod3 = false;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMethod3 = true;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMaxSalMethod3 = true;
                        }
                        else if (y.Equals("usetopkopticalflowavesalmethod3"))
                        {
                            opticalFlowCamerasController.ifUseMaxBlendMethod3 = false;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMethod3 = true;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMaxSalMethod3 = false;
                        }
                        else if (y.Equals("useavemethod3"))
                        {
                            opticalFlowCamerasController.ifUseMaxBlendMethod3 = false;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMethod3 = false;
                            opticalFlowCamerasController.ifUseTopKOpticalFlowMaxSalMethod3 = false;
                        }
                        else {
                            Debug.LogError("error dpSalStrategy");
                        }
                        break;
                    case "pathstrategy":
                        y = y.ToLower();
                        if (y.Equals("dp"))
                        {
                            opticalFlowCamerasController.ifInitPathUseGreedy = false;
                        }
                        else if (y.Equals("greedy"))
                        {
                            opticalFlowCamerasController.ifInitPathUseGreedy = true;
                        }
                        else{
                            Debug.LogError("error pathStrategy");
                        }
                        break;
                    case "output":
                        opticalFlowCamerasController.PreparePath();
                        if (!Directory.Exists(outputRootDir))
                            Directory.CreateDirectory(outputRootDir);
                        var dir = outputRootDir + "/" + y;
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        rootDir = dir;
                        var camPathFile = rootDir + "/cp3.json";
                        opticalFlowCamerasController.SaveCameraPaths(camPathFile);
                        var timePathFile = rootDir + "/time.txt";
                        var timeContent = "";
                        var ofcc = opticalFlowCamerasController;
                        timeContent += "preparePathTotalTime = " + ofcc.preparePathTotalTime + "\n";
                        timeContent += "initPathTotalTime = " + ofcc.initPathTotalTime + "\n";
                        timeContent += "initPathJointDpTime = " + ofcc.initPathJointDpTime + "\n";
                        timeContent += "initPathRefineTime = " + ofcc.initPathRefineTime + "\n";                        
                        timeContent += "smoothPathTime = " + ofcc.smoothPathTime + "\n";
                        timeContent += "renderCamWindowTime = " + ofcc.renderCamWindowTime + "\n";

                        File.WriteAllText(timePathFile, timeContent);
                        ExportSubVideoToDirectory();
                        while (!ifExportSubVideoCoroutineFinished) {
                            yield return null;
                        }
                        break;
                    default:
                        Debug.LogError("error command: " + command);
                        break;
                }
            }
        }
        var timeEnd = DateTime.Now.Ticks;
        Debug.Log("command finished! timeCost: "+ (timeEnd - timeStart) / 1e7);
        ifExecuteCommandFinished = true;
    }
    public void ButtonLoadCommandFileOnClick()
    {
        panelVideoController.PauseVideo();
        string path = "";
        if (GeneralManager.GetCommandPath(out path))
        {
            var sp = path.Split('\\');
            var outputPath = path.Remove(path.Length - sp[sp.Length - 1].Length - 1) + "/outputOfCmd";
            Debug.Log("outputPath: " + outputPath);
            var timeStart = DateTime.Now.Ticks;
            StartCoroutine(ExecuteCommandFromFile(path, outputPath));
            var timeEnd = DateTime.Now.Ticks;
            Debug.Log("Load Command timeCost: " + (timeEnd - timeStart) / 1e7);
        }
        CloseAllMenuBarButtonPanels();
    }
    public void LoadDataAndExecuteCommand(string path) {        
        var commandPath = path + "/command.txt";
        if (File.Exists(commandPath))
        {
            LoadDataByOneClick(path);
            var outputDir = path+"/outputOfCmd";
            StartCoroutine(ExecuteCommandFromFile(commandPath, outputDir));
        }
        else {
            Debug.Log("no command file!");
            ifExecuteCommandFinished = true;
        }
    }
    public void ButtonLoadDataAndExecuteCommandOnClick() {
        panelVideoController.PauseVideo();
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择文件夹",
            ShowNewFolderButton = false  
        };        
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            path = fb.SelectedPath;
            Debug.Log("path: " + path);
            LoadDataAndExecuteCommand(path);
        }
        else
        {
            Debug.Log("cancel!");
            return;
        }
        fb.Dispose();
        CloseAllMenuBarButtonPanels();
    }
    public IEnumerator ExeCmdForVideos(string path) {
        var timeStart = DateTime.Now.Ticks;
        var dirList = Directory.GetDirectories(path);
        foreach (var dir in dirList) {            
            LoadDataAndExecuteCommand(dir);
            while (!ifExecuteCommandFinished)
                yield return null;
            Debug.Log(dir + " finished!");
        }            
        var timeEnd = DateTime.Now.Ticks;
        Debug.Log("ExeCmdForVideos finished! timeCost: " + (timeEnd - timeStart) / 1e7);
    }
    public void ButtonExeCmdForVideosOnClick()
    {
        panelVideoController.PauseVideo();
        System.Windows.Forms.FolderBrowserDialog fb = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "ChooseDir",
            ShowNewFolderButton = false   
        };
        string path = "";
        if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            path = fb.SelectedPath;
            StartCoroutine(ExeCmdForVideos(path));
        }
        else
        {
            Debug.Log("cancel!");
            return;
        }
        fb.Dispose();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonCollectGarbage()
    {
        panelVideoController.PauseVideo();
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonInitKoreaMethod()
    {
        manager.panoramaVideoController.InitKoreaMethod();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonInitMultiMethod()
    {
        manager.panoramaVideoController.InitMultiMethod();
        CloseAllMenuBarButtonPanels();
    }
    public void ButtonInitKoreaPlusMultiMethod()
    {
        manager.panoramaVideoController.InitKoreaPlusMultiMethod();
        CloseAllMenuBarButtonPanels();
    }

    public void ButtonInitKoreaPlusMultiPlusPipMethod()
    {
        manager.panoramaVideoController.InitKoreaPlusMultiPlusPipMethod();
        CloseAllMenuBarButtonPanels();
    }

    public void ButtonEnterSubWindowSceneOnClick() {
        SceneManager.LoadScene("SubWindowScene");
        if (manager.panoramaVideoController != null) {
            //manager.panoramaVideoController.dontDestroyOnLoad.StoreData(manager);
            manager.panoramaVideoController.ChangeTitleName(Manager.productName);
        }
        //Debug.Log("LoadScene Method23Scene");
    }
    public void ButtonEnterNoSubWindowSceneOnClick()
    {
        SceneManager.LoadScene("NoSubWindowScene");
        if (manager.panoramaVideoController != null)
        {
            //manager.panoramaVideoController.dontDestroyOnLoad.StoreData(manager);
            manager.panoramaVideoController.ChangeTitleName(Manager.productName);
        }
        //Debug.Log("LoadScene Method14Scene");
    }
}
