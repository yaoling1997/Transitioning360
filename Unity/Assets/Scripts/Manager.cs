using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

public class Manager : MonoBehaviour {    
    public static readonly string[] sceneNames = new string[] { "AnnotateVideoScene", "ViewerScene", "NoSubWindowScene", "SubWindowScene" };
    public static readonly string productName = "Video360";
    //A keyframe contains four frames
    public Camera cameraCalculate;
    public GameObject menuBar;    
    public GameObject panelVideoControllerBackground;
    public RawImage mainNFOV23Content;
    public PanelVideoController panelVideoController;
    public Material videoPanoramicMaterial;//Panorama material, spherical material    
    public VideoPlayer videoPlayer;
    public RawImage[] rawImageVideoContent;
    public OpticalFlowCamerasController opticalFlowCamerasController;    
    public GameObject scrollviewNFOVsContent;
    public RawImage mainNFOVContent;
    public MainNFOVController mainNFOVController;
    public ComputeShader initialPathPlanningComputeShader;
    public ComputeShader initialPathPlanningMethod3ComputeShader;
    public PanoramaVideoController panoramaVideoController;
    public Camera mainCamera;
    public Camera mainNFOVCamera;
    public Sprite pauseImage;
    public Sprite continueImage;
    public Sprite voiceOnImage;
    public Sprite voiceOffImage;
    public Text textMethodStatus;
    public Text textCurrentMethodName;

    public GameObject prefabPanelVideoEndCover;    
    public GameObject prefabPanelLoadingCover;
    public GameObject prefabPanelTestStartCover;

    private void Awake()
    {        
        Debug.Log("textMethodStatus: " + textMethodStatus);
        
    }
    // Use this for initialization
    void Start () {        
    }
	
	// Update is called once per frame
	void Update () {
		
	}

    public static int GetActivateSceneNumber() {//0 AnnotateVideoScene; 1 ViewerScene; 2 ComparisonScene
        var sceneName = SceneManager.GetActiveScene().name;
        for (int i = 0; i < sceneNames.Length; i++)
            if (sceneNames[i].Equals(sceneName))
                return i;
        return -1;
    }

    public void VideoTextureChanges() {
        videoPanoramicMaterial.mainTexture = videoPlayer.targetTexture;
        foreach (var item in rawImageVideoContent) {
            item.texture= videoPlayer.targetTexture;
        }        
        //Debug.Log(string.Format("videoTexture.size: {0},{1}", videoPanoramicMaterial.mainTexture.width, videoPanoramicMaterial.mainTexture.height));
    }
    public static void DestroyTexture2dList(List<Texture2D> list) {
        if (list == null)
            return;
        foreach (var t in list) {
            if (t!=null)
                Destroy(t);
        }
        list.Clear();
    }
    public static void DestroyTexture2dList(List<List<Texture2D>> list)
    {
        if (list == null)
            return;
        foreach (var l in list) {            
            DestroyTexture2dList(l);
        }
        list.Clear();
    }
}
