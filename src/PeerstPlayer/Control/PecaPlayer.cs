﻿using System;
using System.ComponentModel;
using System.Windows.Forms;
using PeerstLib.Bbs;
using PeerstLib.PeerCast;
using PeerstLib.Utility;
using WMPLib;

namespace PeerstPlayer.Control
{
	//-------------------------------------------------------------
	// 概要：ペカプレイヤークラスコントロール
	// 詳細：PeerCast対応の動画再生プレイヤー
	//-------------------------------------------------------------
	public partial class PecaPlayer : UserControl
	{
		//-------------------------------------------------------------
		// 公開プロパティ
		//-------------------------------------------------------------

		// チャンネル情報
		public ChannelInfo ChannelInfo { get; set; }
		public event EventHandler ChannelInfoChange = delegate { };

		// 音量
		public int Volume
		{
			get { return wmp.settings.volume; }
			set
			{
				wmp.settings.volume = value;
				VolumeChange(this, new EventArgs());
			}
		}
		public event EventHandler VolumeChange = delegate { };

		// ミュート
		public bool Mute
		{
			get { return wmp.settings.mute; }
			set
			{
				wmp.settings.mute = value;
				VolumeChange(this, new EventArgs());
			}
		}

		// 再生時間
		public string Duration
		{
			get
			{
				string position = wmp.Ctlcontrols.currentPositionString;
				if (position.Length == 5)
				{
					position = "00:" + position;
				}

				return position;
			}
		}

		// バッファー率
		public int BufferingProgress
		{
			get { return wmp.network.bufferingProgress; }
		}

		// 再生状態
		public WMPPlayState PlayState
		{
			get { return wmp.playState; }
		}

		// クリック開始位置
		public System.Drawing.Point ClickPoint { get; set; }

		// マウス押下イベント
		public event AxWMPLib._WMPOCXEvents_MouseDownEventHandler MouseDownEvent
		{
			add { wmp.MouseDownEvent += value; }
			remove { wmp.MouseDownEvent -= value; }
		}

		// マウスアップイベント
		public event AxWMPLib._WMPOCXEvents_MouseUpEventHandler MouseUpEvent
		{
			add { wmp.MouseUpEvent += value; }
			remove { wmp.MouseUpEvent -= value; }
		}

		// マウス移動イベント
		public event AxWMPLib._WMPOCXEvents_MouseMoveEventHandler MouseMoveEvent
		{
			add { wmp.MouseMoveEvent += value; }
			remove { wmp.MouseMoveEvent -= value; }
		}

		// ダブルクリックイベント
		public event EventHandler DoubleClickEvent = delegate { };

		// WMPのハンドル
		public IntPtr WMPHandle { get { return wmp.Handle; } }

		// コンテキストメニューの有効
		public bool EnableContextMenu
		{
			get { return wmp.enableContextMenu; }
			set { wmp.enableContextMenu = value; }
		}

		//-------------------------------------------------------------
		// 非公開プロパティ
		//-------------------------------------------------------------

		// PeerCast通信
		private PeerCastConnection pecaConnect = null;

		// チャンネル更新用
		private BackgroundWorker updateChannelInfoWorker = new BackgroundWorker();

		//-------------------------------------------------------------
		// 定義
		//-------------------------------------------------------------

		// チャンネル更新間隔
		private const int UpdateInterval = 60000;

		//-------------------------------------------------------------
		// 概要：コンストラクタ
		// 詳細：コントロールの初期化
		//-------------------------------------------------------------
		public PecaPlayer()
		{
			Logger.Instance.Debug("PecaPlayer()");
			InitializeComponent();

			// 初期設定
			wmp.uiMode = "none";
			wmp.stretchToFit = true;
			wmp.enableContextMenu = false;

			// キャンセル許可
			updateChannelInfoWorker.WorkerSupportsCancellation = true;

			// ダブルクリックイベント
			new WmpNativeWindow(wmp.Handle).DoubleClick += (sender, e) => DoubleClickEvent(sender, e);

			// チャンネル自動リトライ
			new ChannelAutoRetry(wmp);

			// WMPフルスクリーンを無効
			wmp.MouseDownEvent += (sender, e) =>
			{
				// クリック開始位置を設定
				ClickPoint = new System.Drawing.Point(e.fX, e.fY);

				// WMPのフルクリーンを解除
				if (wmp.fullScreen)
				{
					wmp.fullScreen = false;
				}
			};
		}

		//-------------------------------------------------------------
		// 概要：指定URLを再生
		// 詳細：動画を再生し、チャンネル情報を取得する
		//-------------------------------------------------------------
		public void Open(string streamUrl)
		{
			Logger.Instance.DebugFormat("Open(streamUrl:{0})", streamUrl);

			StreamUrlInfo info = StreamUrlAnalyzer.GetUrlInfo(streamUrl);
			pecaConnect = new PeerCastConnection(info);

			// 動画の再生
			wmp.URL = streamUrl;

			// 再生変更イベント
			wmp.OpenStateChange += (sender, e) =>
			{
				Logger.Instance.Debug(String.Format("OpenStateChange [{0}]", wmp.openState.ToString()));
				// 動画切替時に、音量が初期化されるための対応
				// TODO ミュート時に音量が変わらないようにする
				VolumeChange(this, new EventArgs());
			};

			// チャンネル更新スレッド
			updateChannelInfoWorker.DoWork += (sender, e) =>
			{
				while (true)
				{
					Logger.Instance.Debug("チャンネル更新トライ");
					ChannelInfo chInfo = pecaConnect.GetChannelInfo();
					if (!String.IsNullOrEmpty(chInfo.Name))
					{
						ChannelInfo = chInfo;
						return;
					}

					System.Threading.Thread.Sleep(3000);
				}
			};
			updateChannelInfoWorker.RunWorkerCompleted += (sender, e) =>
			{
				Logger.Instance.InfoFormat("チャンネル更新完了 [チャンネル名:{0}]", ChannelInfo.Name);
				ChannelInfoChange(sender, e);
			};

			// キー押下イベント
			wmp.KeyDownEvent += (sender, e) =>
			{
				// TODO イベントを通知する
				if (e.nKeyCode == (short)Keys.T)
				{
					ParentForm.TopMost = !ParentForm.TopMost;
				}
			};

			// チャンネル情報の取得
			Timer timer = new Timer();
			timer.Interval = UpdateInterval;
			timer.Tick += (sender, e) =>
			{
				// チャンネル更新
				if (!updateChannelInfoWorker.IsBusy)
				{
					Logger.Instance.Info("チャンネル更新開始");
					updateChannelInfoWorker.RunWorkerAsync();
				}

				// メモリリーク防止
				if (wmp.Ctlcontrols != null)
				{
					wmp.Ctlcontrols.play();
				}
			};
			timer.Start();

			// チャンネル更新
			if (!updateChannelInfoWorker.IsBusy)
			{
				Logger.Instance.Info("チャンネル更新開始");
				updateChannelInfoWorker.RunWorkerAsync();
			}
		}

		//-------------------------------------------------------------
		// 概要：終了処理
		//-------------------------------------------------------------
		public void Close()
		{
			Logger.Instance.Debug("Close()");
			if (updateChannelInfoWorker.IsBusy)
			{
				updateChannelInfoWorker.CancelAsync();
			}
		}
	}
}
