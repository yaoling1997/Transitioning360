## Transitioning360: Content-aware NFoV Virtual Camera Paths for 360° Video Playback


#### Miao Wang, Yi-Jun Li, Wen-Xuan Zhang, Christian Richardt, Shi-Min Hu.

![GitHub search hit counter](https://img.shields.io/github/search/yaoling1997/Transitioning360/goto)

<p align="center"> 
<img src="https://github.com/yaoling1997/Transitioning360/blob/master/doc/media/3InterestingMen666.gif?raw=true">
<br>
<br>
We present <i>Transitioning360</i>, a tool for 360° video navigation and playback on 2D displays by transitioning between multiple NFoV views that track potentially interesting targets or events. Our method computes virtual NFoV camera paths considering content awareness and diversity in an offline preprocess. During playback, the user can watch any NFoV view corresponding to a precomputed camera path. Moreover, our interface shows other candidate views, providing a sense of concurrent events. At any time, the user can transition to other candidate views for fast navigation and exploration.
<br>
- <a href="https://vimeo.com/456945096">Video</a> - <a href="https://researchportal.bath.ac.uk/files/211657571/Transitioning360_WangEtAl_ISMAR2020.pdf">Paper</a> - <a href="https://github.com/yaoling1997/Transitioning360/tree/master/Unity">Code</a> - </p>

Platform: Win-64

Unity version: 2018.2.13f1

[Big Sample Data]()

#####For Annotation

1.Open "AnnotateVideoScene".

2.Go to "File" -> Click "Load Data Execute Cmd" -> Choose data directory.

3.Wait...

4.Results will show up in the same directory. 

For Annotation, it's better to run in the editor mode.

#####For Interaction

1.Open either "SubWindowScene" or "NoSubWindowScene".

2.Choose interaction method shown in the dropdown.

3.Click "Load" -> Choose data directory.

4.wait...

5.Click "Start".

For Interaction, it is better to run after built.