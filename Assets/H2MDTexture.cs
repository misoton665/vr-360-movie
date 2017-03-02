/* H2MD Unity Plugin Movie Texture Class */
/* Copyright 2016 AXELL CORPORATION */

/* 任意のオブジェクトにこのスクリプトをアタッチした後、pathにH2MDのファイル名を指定することで動画を再生 */

using UnityEngine;
using System.Collections;
using System.IO;
using System;

/* ハンドル定義　*/
using H2MDDecoderHandle = System.IntPtr;

public class H2MDTexture : MonoBehaviour {
	/* 設定項目 */
	public String path;				/* StreamingAssetsに置いたH2MDファイルへのパス (sample.h2mdなど) */
	public String texture_id="";	/* 設定先のテクスチャID、設定していない場合はmainTexture */
	private H2MDMovie m_movie=null; /* H2MD ムービーオブジェクト */
	private double m_time=0;		/* デコードフレームを決定するためのタイムカウンタ */

	/* AndroidではStreamingAssets経由ではアクセスできないためコールバック経由でロードする */

	IEnumerator StreamingAssetsRequestGetCode(string path){
		/* WWWクラス経由でStreamingAssetsを読み込む */

		#if UNITY_ANDROID && !UNITY_EDITOR
		string prefix="";
		#else
		string prefix="file://";
		#endif
		WWW www = new WWW(prefix+path);
		yield return www;
		if(www.bytes.Length==0){
			Debug.Log(""+prefix+path+" not found");
		}

		OpenMovie(null,www.bytes);
	}

	void Start () {
		/* Assets/StreamingASsets/[path]から動画を読み込み */

		if(path==""){
			Debug.Log("Please set H2MD file path");
			return;
		}

		m_movie=new H2MDMovie();

		string filePath = Application.streamingAssetsPath+"/"+path;

		#if UNITY_ANDROID
		/* Androidの場合はコールバック経由で読み込む */
		StartCoroutine(StreamingAssetsRequestGetCode(filePath));
		#else
		/* Android以外の場合はfopenで読み込む */
		OpenMovie(filePath,null);
		#endif
	}

	void OpenMovie(String filePath,byte [] code) {
		/* VR向けに非同期でデコードする場合は有効化 */
		/* m_movie.SetAsyncDecodeMode(true); */

		Texture2D tex;
		if(code != null){
			tex=m_movie.OpenMem(code);
		}else{
			tex=m_movie.Open(filePath);
		}

		if(tex==null){
			Debug.Log("H2MD file not found");
			Application.Quit();
			return;
		}

		/* テクスチャをアタッチ */

		if(texture_id!=""){
			GetComponent<Renderer>().material.SetTexture(texture_id,tex);
		}else{
			GetComponent<Renderer>().material.mainTexture=tex;
		}
	}
	
	void Update () {
		/* デコードフレームを決定する */

		if(m_movie==null){
			return;
		}
	
		int frame=(int)(m_time*m_movie.GetFrameRate());
		m_time+=Time.deltaTime;

		if(frame>=m_movie.GetTotalFrames()){
			m_time=0;
			frame=0;
		}

		/* デコードを行ってテクスチャを更新する */

		m_movie.Decode(frame);
		m_movie.GetImage();
	}

	void OnDestroy () {
		/* デコーダを開放 */

		if(m_movie!=null){
			m_movie.Dispose();
			m_movie=null;
		}
	}
	
	void OnApplicationQuit () {
		/* デコーダを開放 */

		if(m_movie!=null){
			m_movie.Dispose();
			m_movie=null;
		}
	}
}