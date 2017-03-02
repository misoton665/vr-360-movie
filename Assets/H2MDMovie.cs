/* H2MD Unity Plugin Movie Decode Class */
/* Copyright 2016 AXELL CORPORATION */

using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;
using System.IO;
using System;
using System.Threading;

public class H2MDMovie{
	private Texture2D m_tex;						/* テクスチャ */
	private H2MDDecoder.h2mdMovieInfo m_movie_info;	/* 動画情報 */
	private object lock_object = new object();		/* 非同期データ供給のロック */
	private System.IntPtr m_decoder;				/* デコーダインスタンス */
	private bool m_is_async_decode = false;			/* 非同期デコードモードかどうか */

	private Color32[] m_image_color32;				/* デコード先カラーバッファ */
	private GCHandle m_image_handle;				/* デコード先バッファへのハンドル */
	private IntPtr m_rgbquad_image;					/* デコード先バッファへのポインタ */

	private byte [] m_buf;							/* メモリデコード用コードバッファ */
	private GCHandle m_handle;						/* メモリデコード用コードバッファ */
	private IntPtr m_buf_ptr;						/* メモリデコード用コードバッファ */

	public H2MDMovie(){
		m_decoder=H2MDDecoder.DECODER_NULL;
		m_movie_info=new H2MDDecoder.h2mdMovieInfo();
	}
	
	~H2MDMovie(){
		Dispose();
	}
	
	/** 
	 *  StreamingAssetsフォルダから動画を読み込みます
	 *  引数：
	 *  　 path     - StreamingAssetsフォルダに置いた動画へのパス
	 *  返値：
	 *   　成功時 - デコード結果の格納されるTexture2Dオブジェクト
	 *   　失敗時 - null
	 */

	public Texture2D Open(String path){
		int status;

		/* 既存のデコーダインスタンスを開放 */

		Dispose();

		/* デコーダインスタンスの作成 */

		status=H2MDDecoder.h2mdCreate(ref m_decoder,H2MDDecoder.H2MDDEC_IMAGE_RGBA,H2MDDecoder.H2MDDEC_MULTITHREAD_AUTO);
		if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
			return null;
		}

		/* ファイルを開く */

		status=H2MDDecoder.h2mdOpenStreamFileA(m_decoder,path);
		if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
			return null;
		}

		return OpenCore();
	}

	/** 
	 *  メモリから動画を読み込みます
	 *  引数：
	 *  　 code   - H2MDファイルの実体
	 *  返値：
	 *   　成功時 - デコード結果の格納されるTexture2Dオブジェクト
	 *   　失敗時 - null
	 */

	public Texture2D OpenMem(byte [] code){
		int status;

		/* 既存のデコーダインスタンスを開放 */

		Dispose();

		/* デコーダインスタンスの作成 */

		status=H2MDDecoder.h2mdCreate(ref m_decoder,H2MDDecoder.H2MDDEC_IMAGE_RGBA,H2MDDecoder.H2MDDEC_MULTITHREAD_AUTO);
		if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
			return null;
		}

		/* ファイルを開く */

		m_buf = code; /* GC対象から除外 */
		m_handle = GCHandle.Alloc(m_buf, GCHandleType.Pinned);
		m_buf_ptr = m_handle.AddrOfPinnedObject();

		status=H2MDDecoder.h2mdOpenStreamMem(m_decoder,m_buf_ptr,m_buf.Length);
		if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
			return null;
		}

		return OpenCore();
	}

	private Texture2D OpenCore(){
		/* テクスチャを作成 */

		int status=H2MDDecoder.h2mdGetMovieInfo(m_decoder,m_movie_info,H2MDDecoder.H2MDDEC_MOVIE_INFO_VERSION);
		if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
			return null;
		}

		/* MipMapを作成すると速度が低下するため、MipMapを無効化 */

		m_tex = new Texture2D((int)m_movie_info.width,(int)m_movie_info.height,TextureFormat.ARGB32,false);

		/* 非同期デコード */

		if(m_is_async_decode){
			CreateAsync();
		}

		return m_tex;
	}

	/**
	 *  動画の1フレームをデコードします
	 *  引数：
	 *　   frame_no - デコードするフレーム番号
	 *  返値：
	 *   　成功時   - H2MDDEC_STATUS_SUCCESS
	 *　   失敗時   - エラーコード
	 */

	public int Decode(int frame_no){
		if(m_decoder==H2MDDecoder.DECODER_NULL){
			return H2MDDecoder.H2MDDEC_STATUS_INVALID_STATE;
		}

		lock(lock_object){
			/* 非同期デコードモード */

			if(m_is_async_decode){
				return DecodeAsync(frame_no);
			}

			/* フレームのデコード */

			int status=H2MDDecoder.h2mdDecode(m_decoder,frame_no);
			if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
				return status;
			}

			return H2MDDecoder.H2MDDEC_STATUS_SUCCESS;
		}
	}

	/**
	 *  デコードしたフレームをテクスチャに転送します
	 *  返値：
	 *   　成功時   - H2MDDEC_STATUS_SUCCESS
	 *　   失敗時   - エラーコード
	 */

	public int GetImage(){
		if(m_decoder==H2MDDecoder.DECODER_NULL){
			return H2MDDecoder.H2MDDEC_STATUS_INVALID_STATE;
		}

		lock(lock_object){
			/* 非同期デコードモード */

			if(m_is_async_decode){
				if(!IsReadyGetImage()){
					return H2MDDecoder.H2MDDEC_STATUS_SUCCESS;
				}
				int async_status=GetAndClearAsyncDecodeStatus();
				if(async_status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
					return async_status;
				}
			}

			/* ネイティブ向けにデコードした画像をテクスチャに転送 */

			if(m_image_color32==null){
				m_image_color32 = m_tex.GetPixels32();
				m_image_handle = GCHandle.Alloc(m_image_color32, GCHandleType.Pinned);
				m_rgbquad_image = m_image_handle.AddrOfPinnedObject();
			}

			int image_stride=(int)(m_movie_info.width*4);
			int status=H2MDDecoder.h2mdGetImage(m_decoder,m_rgbquad_image,(int)(m_movie_info.height*image_stride),image_stride);

			if(status!=H2MDDecoder.H2MDDEC_STATUS_SUCCESS){
				return status;
			}

			m_tex.SetPixels32( m_image_color32 );
			m_tex.Apply();

			return H2MDDecoder.H2MDDEC_STATUS_SUCCESS;
		}
	}
	
	/**
	 *  非同期デコードモードを設定します
	 *  引数:
	 *    is_async - trueで非同期デコード、falseで同期デコード
	 *  解説：
	 *    非同期デコードモードに設定すると別スレッドでデコードを行います。
	 */
	 
	 public void SetAsyncDecodeMode(bool is_async){
	 	m_is_async_decode=is_async;
	 }

	/**
	 *  動画のフレーム数を取得します
	 *  返値：
	 *   　動画のフレーム数
	 */

	public int GetTotalFrames(){
		return (int)m_movie_info.total_frames;
	}

	/**
	 *  動画のフレームレートを取得します
	 *  返値：
	 *   　動画のフレームレート
	 */

	public float GetFrameRate(){
		if(m_movie_info.fps_denominator==0){
			return 1.0f;
		}
		return 1.0f*m_movie_info.fps_numerator/m_movie_info.fps_denominator;
	}

	/**
	 *  インスタンスを開放します
	 */

	public void Dispose(){
		DisposeAsync();
		lock(lock_object){
			if(m_decoder!=H2MDDecoder.DECODER_NULL){
				H2MDDecoder.h2mdDestroy(m_decoder);
				m_decoder=H2MDDecoder.DECODER_NULL;
			}
			m_image_handle.Free();
			m_image_color32=null;
			m_tex=null;
		}
	}

	/**
	 *  非同期デコードの結果が存在するかどうかを取得します
	 *  返値：
	 *    存在する場合 true、存在しない場合 false
	 */

	public bool IsReadyGetImage(){
		if(!m_is_async_decode){
			return true;
		}
		return (m_async_result!=ASYNC_EMPTY);
	}

	/**
	 *  非同期デコードインタフェース
	 *  別スレッドで動画をデコードすることでVRアプリの体感速度を向上
	 */

	private UnityEngine.Object m_lock_async = new UnityEngine.Object ();

	private const int ASYNC_EMPTY = -1;

	private int m_async_request = ASYNC_EMPTY;
	private int m_async_result  = ASYNC_EMPTY;

	private int m_async_decode_status = H2MDDecoder.H2MDDEC_STATUS_SUCCESS;

	private Thread m_thread = null;
	private AutoResetEvent m_auto_event = null;

	private void CreateAsync(){
		m_auto_event=new AutoResetEvent(false);
		m_thread  = new Thread (Worker);
		m_thread.Start();
	}

	private bool m_thread_abort=false;

	private void DisposeAsync(){
		while(m_async_request!=ASYNC_EMPTY){
			Thread.Sleep(1);
		}
		if(m_thread!=null){
			m_thread_abort=true;
			m_auto_event.Set();
			while(m_thread.IsAlive){
				Thread.Sleep(1);
			}
			m_thread_abort=false;
			
			m_thread=null;
		}
	}

	private int DecodeAsync(int frame_no){
		if(frame_no==ASYNC_EMPTY){
			return H2MDDecoder.H2MDDEC_STATUS_INVALID_ARGUMENT;
		}
		lock(m_lock_async){
			if(m_async_request==ASYNC_EMPTY){
				m_async_request=frame_no;
				m_auto_event.Set();
			}
		}
		return H2MDDecoder.H2MDDEC_STATUS_SUCCESS;
	}

	private int GetAndClearAsyncDecodeStatus(){
		lock (m_lock_async) {
			if(m_async_result!=ASYNC_EMPTY){
				m_async_result=ASYNC_EMPTY;
				int status=m_async_decode_status;
				m_async_decode_status=H2MDDecoder.H2MDDEC_STATUS_SUCCESS;
				return status;
			}
		}
		return H2MDDecoder.H2MDDEC_STATUS_INVALID_STATE;
	}

	private void Worker (object arguments){
		while (true) {
			m_auto_event.WaitOne();
			if(m_thread_abort){
				return;
			}

			while(true){
				lock (m_lock_async) {
					if(m_async_request==ASYNC_EMPTY){
						break;
					}
				}
			
				int status=H2MDDecoder.h2mdDecode (m_decoder, m_async_request);

				lock (m_lock_async) {
					m_async_decode_status=status;
					m_async_result=m_async_request;
					m_async_request=ASYNC_EMPTY;
				}
			}
		}
	}
}