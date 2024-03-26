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

/** 인앱 결제 관리자 - 복원 */
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener, IDetailedStoreListener
{
	#region 함수
	/** 상품을 복원한다 */
	public void ProductsRestore(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback)
	{
		CFunc.ShowLog("CPurchaseManager.ProductsRestore", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
		// 상품 복원이 불가능 할 경우
		if(!this.IsInit || m_bIsPurchasing)
		{
			goto PURCHASE_M_PRODUCTS_RESTORE_EXIT;
		}

		m_oCallbackDictB.ExReplaceVal(ECallbackPurchase.RESTORE, a_oCallback);

#if UNITY_IOS
		var oStoreExtension = m_oProviderExtension.GetExtension<IAppleExtensions>();
#else
		var oStoreExtension = m_oProviderExtension.GetExtension<IGooglePlayStoreExtensions>();
#endif // #if UNITY_IOS

		oStoreExtension.RestoreTransactions(this.OnProductsRestore);
		return;
#endif // #if UNITY_EDITOR || UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)

PURCHASE_M_PRODUCTS_RESTORE_EXIT:
		CFunc.Invoke(ref a_oCallback, this, null, false);
	}

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	/** 상품이 복원되었을 경우 */
	private void OnProductsRestore(bool a_bIsSuccess)
	{
		CFunc.ShowLog($"CPurchaseManager.OnProductsRestore: {a_bIsSuccess}", KCDefine.B_LOG_COLOR_PLUGIN);
		CScheduleManager.Inst.AddCallback(KCDefine.G_PURCHASE_M_KEY_CALLBACK_PRODUCTS_RESTORE, () => this.HandleOnProductsRestore(a_bIsSuccess));
	}

	/** 상품 복원을 처리한다 */
	private void HandleOnProductsRestore(bool a_bIsSuccess)
	{
		var oProductList = new List<Product>();

		// 상품 복원에 실패했을 경우
		if(!a_bIsSuccess)
		{
			goto PURCHASE_M_HANDLE_ON_PRODUCTS_RESTORE_EXIT;
		}

		for(int i = 0; i < m_oControllerStore.products.all.Length; ++i)
		{
			// 결제 된 비소모 상품 일 경우
			if(this.IsPurchaseProductConsumableNon(m_oControllerStore.products.all[i]))
			{
				oProductList.ExAddVal(m_oControllerStore.products.all[i]);
			}

			this.RemoveIDProductPurchase(m_oControllerStore.products.all[i].definition.id);
		}

PURCHASE_M_HANDLE_ON_PRODUCTS_RESTORE_EXIT:
		var oCallbackRestore = m_oCallbackDictB.GetValueOrDefault(ECallbackPurchase.RESTORE);
		oCallbackRestore?.Invoke(this, oProductList, a_bIsSuccess && oProductList.ExIsValid());
	}
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 함수
}
#endif // #if PURCHASE_MODULE_ENABLE
