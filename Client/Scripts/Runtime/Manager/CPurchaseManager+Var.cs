using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

#if PURCHASE_MODULE_ENABLE
using System.IO;
using System.Threading.Tasks;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Unity.Services.Core;
using Unity.Services.Core.Environments;

#if RECEIPT_CHECK_ENABLE && (UNITY_IOS || UNITY_ANDROID)
using UnityEngine.Purchasing.Security;
#endif // #if RECEIPT_CHECK_ENABLE && (UNITY_IOS || UNITY_ANDROID)

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Purchasing;
#endif // #if UNITY_EDITOR

/** 인앱 결제 관리자 - 변수 */
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener, IDetailedStoreListener
{
	#region 변수
	private bool m_bIsPurchasing = false;
	private List<string> m_oListIDProductPurchase = new List<string>();

	private Dictionary<ECallbackPurchase, System.Action<CPurchaseManager, string, bool>> m_oCallbackDictA = new Dictionary<ECallbackPurchase, System.Action<CPurchaseManager, string, bool>>();
	private Dictionary<ECallbackPurchase, System.Action<CPurchaseManager, List<Product>, bool>> m_oCallbackDictB = new Dictionary<ECallbackPurchase, System.Action<CPurchaseManager, List<Product>, bool>>();

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	private IStoreController m_oControllerStore = null;
	private IExtensionProvider m_oProviderExtension = null;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 변수

	#region 프로퍼티
	public STParams Params { get; private set; }
	public bool IsInit { get; private set; } = false;
	#endregion // 프로퍼티
}
#endif // #if PURCHASE_MODULE_ENABLE
