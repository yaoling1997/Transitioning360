using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VideoContentController : MonoBehaviour {
    private RawImage videoContentRawImage;
    private Image videoContentImage;
    void Start () {
        videoContentRawImage = GetComponent<RawImage>();
        videoContentImage = GetComponent<Image>();
    }
	
	// Update is called once per frame
	void Update () {
        UpdateVideoContent();
    }
    public void UpdateVideoContent()
    {
        var prts = transform.parent.GetComponent<RectTransform>().rect.size;
        var rt = GetComponent<RectTransform>();
        var size = new Vector2(1,1);
        if (videoContentRawImage != null)
        {            
            var t = videoContentRawImage.mainTexture;
            if (t != null)
                size = new Vector2(t.width, t.height);
        }
        else {
            if (videoContentImage.sprite != null) {
                var t = videoContentImage.sprite.texture;
                if (t!=null)
                    size = new Vector2(t.width, t.height);
            }
        }
        var w = Mathf.Min(prts.x, prts.y * size.x / size.y);
        var h = Mathf.Min(prts.y, prts.x * size.y / size.x);
        rt.sizeDelta = new Vector2(w, h);
    }

}
