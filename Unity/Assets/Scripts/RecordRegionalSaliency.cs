using System.Collections.Generic;
using Newtonsoft.Json;

public class RecordRegionalSaliency{
    public int downsampleWidth;
    public int downsampleHeight;
    public List<float[,]> regionalSaliency;
    public static RecordRegionalSaliency CreateFromJSON(string jsonString)
    {
        return (RecordRegionalSaliency)JsonConvert.DeserializeObject(jsonString, typeof(RecordRegionalSaliency));
    }
    public string SaveToString()
    {
        return JsonConvert.SerializeObject(this);

    }

}
