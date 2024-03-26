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
	/** 콜백 */
	public enum ECallback
	{
		NONE = -1,
		INIT,
		[HideInInspector] MAX_VAL
	}

	/** 결제 콜백 */
	private enum ECallbackPurchase
	{
		NONE = -1,
		RESTORE,
		PURCHASE,
		[HideInInspector] MAX_VAL
	}

	/** 매개 변수 */
	public struct STParams
	{
		public List<STProductInfo> m_oListInfoProduct;
		public Dictionary<ECallback, System.Action<CPurchaseManager, bool>> m_oCallbackDict;
	}
}
#endif // #if PURCHASE_MODULE_ENABLE
