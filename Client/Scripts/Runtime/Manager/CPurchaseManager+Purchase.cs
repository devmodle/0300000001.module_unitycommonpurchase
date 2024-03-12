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

/** 인앱 결제 관리자 - 결제 */
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener, IDetailedStoreListener
{
	#region IStoreListener
	/** 결제되었을 경우 */
	public virtual PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs a_oArgs)
	{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		string oProductID = a_oArgs.purchasedProduct.definition.id;
		CFunc.ShowLog($"CPurchaseManager.ProcessPurchase: {oProductID}", KCDefine.B_LOG_COLOR_PLUGIN);

		try
		{
			// 결제 중 일 경우
			if(m_bIsPurchasing)
			{
				this.AddPurchaseProductID(oProductID);
			}

#if !UNITY_EDITOR && RECEIPT_CHECK_ENABLE && (UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM))
			var oValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
			var oPurchaseReceipts = oValidator.Validate(a_oArgs.purchasedProduct.receipt);

			for(int i = 0; i < oPurchaseReceipts.Length; ++i) {
				CFunc.ShowLog($"CPurchaseManager.ProcessPurchase Validate: {oPurchaseReceipts[i].productID}, {oPurchaseReceipts[i].transactionID}");
			}

			// 결제 영수증이 유효 할 경우
			if(oPurchaseReceipts.ExIsValid()) {
				this.HandlePurchaseResult(oProductID, true);
			} else {
				this.HandlePurchaseResult(oProductID, false, a_bIsComplete: true);
			}

			bool bIsValid = m_bIsPurchasing && oPurchaseReceipts.ExIsValid();
			return bIsValid ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#else
			this.HandlePurchaseResult(oProductID, true);
			return m_bIsPurchasing ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#endif // #if !UNITY_EDITOR && RECEIPT_CHECK_ENABLE && (UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM))
		}
		catch(System.Exception oException)
		{
			CFunc.ShowLogWarning($"CPurchaseManager.ProcessPurchase Exception: {oException.Message}");
		}

		this.HandlePurchaseResult(oProductID, false, a_bIsComplete: true);
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID

		return PurchaseProcessingResult.Complete;
	}

	/** 결제에 실패했을 경우 */
	public virtual void OnPurchaseFailed(Product a_oProduct, PurchaseFailureReason a_eReason)
	{
		var oDesc = new PurchaseFailureDescription(a_oProduct.definition.id, a_eReason, $"{a_eReason}");
		this.OnPurchaseFailed(a_oProduct, oDesc);
	}

	/** 결제에 실패했을 경우 */
	public virtual void OnPurchaseFailed(Product a_oProduct, PurchaseFailureDescription a_oDesc)
	{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		CFunc.ShowLogWarning($"CPurchaseManager.OnPurchaseFailed: {a_oProduct.definition.id}, {a_oDesc.reason}, {a_oDesc.message}");

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_PURCHASE_FAIL_CALLBACK, () =>
		{
			bool bIsPurchaseProductA = this.IsPurchaseNonConsumableProduct(a_oProduct);
			bool bIsPurchaseProductB = a_oDesc.reason == PurchaseFailureReason.DuplicateTransaction;

			bool bIsPurchaseProduct = bIsPurchaseProductA || bIsPurchaseProductB;
			this.HandlePurchaseResult(a_oProduct.definition.id, bIsPurchaseProduct, a_bIsComplete: !bIsPurchaseProduct);
		});
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}
	#endregion // IStoreListener

	#region 함수
	/** 상품을 복원한다 */
	public void RestoreProducts(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback)
	{
		CFunc.ShowLog("CPurchaseManager.RestoreProducts", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
		// 상품 복원이 불가능 할 경우
		if(!this.IsInit || m_bIsPurchasing)
		{
			goto PURCHASE_MANAGER_RESTORE_PRODUCTS_EXIT;
		}

		m_oCallbackDictB.ExReplaceVal(EPurchaseCallback.RESTORE, a_oCallback);

#if UNITY_IOS
		var oStoreExtension = m_oExtensionProvider.GetExtension<IAppleExtensions>();
#else
		var oStoreExtension = m_oExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();
#endif // #if UNITY_IOS

		oStoreExtension.RestoreTransactions(this.OnRestoreProducts);
		return;
#endif // #if UNITY_EDITOR || UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)

PURCHASE_MANAGER_RESTORE_PRODUCTS_EXIT:
		CFunc.Invoke(ref a_oCallback, this, null, false);
	}

	/** 상품을 결제한다 */
	public void PurchaseProduct(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback)
	{
		CFunc.ShowLog($"CPurchaseManager.PurchaseProduct: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		var oProduct = this.GetProduct(a_oID);
		bool bIsEnablePurchase = !m_bIsPurchasing && (oProduct != null && oProduct.availableToPurchase);

		// 상품 결제가 불가능 할 경우
		if(!this.IsInit || bIsEnablePurchase)
		{
			goto PURCHASE_MANAGER_PURCHASE_PRODUCT_EXIT;
		}

		m_bIsPurchasing = true;
		m_oCallbackDictA.ExReplaceVal(EPurchaseCallback.PURCHASE, a_oCallback);

		bool bIsPurchaseProductA = m_oPurchaseProductIDList.Contains(a_oID);
		bool bIsPurchaseProductB = this.IsPurchaseNonConsumableProduct(oProduct);

		// 결제 된 상품 일 경우
		if(bIsPurchaseProductA || bIsPurchaseProductB)
		{
			this.HandlePurchaseResult(a_oID, true);
		}
		else
		{
			m_oStoreController.InitiatePurchase(oProduct, KCDefine.B_PAYLOAD_PURCHASE_M_PURCHASE_PRODUCT);
		}

		return;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID

PURCHASE_MANAGER_PURCHASE_PRODUCT_EXIT:
		CFunc.Invoke(ref a_oCallback, this, a_oID, false);
	}

	/** 결제를 확정한다 */
	public void ConfirmPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback)
	{
		CFunc.ShowLog($"CPurchaseManager.ConfirmPurchase: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.Assert(a_oID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_CONFIRM_PURCHASE_CALLBACK, () =>
		{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
			var oProduct = this.GetProduct(a_oID);
			bool bIsEnableConfirm = this.IsInit && m_bIsPurchasing && (oProduct != null && oProduct.availableToPurchase);

			// 결제 확정이 불가능 할 경우
			if(!bIsEnableConfirm)
			{
				goto PURCHASE_MANAGER_CONFIRM_PURCHASE_EXIT;
			}

			m_oStoreController.ConfirmPendingPurchase(oProduct);

PURCHASE_MANAGER_CONFIRM_PURCHASE_EXIT:
			this.HandlePurchaseResult(a_oID, bIsEnableConfirm, false, true);
			CFunc.Invoke(ref a_oCallback, this, a_oID, bIsEnableConfirm);
#else
			CFunc.Invoke(ref a_oCallback, this, a_oID, false);
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		});
	}

	/** 결제를 거부한다 */
	public void RejectPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback)
	{
		CFunc.ShowLog($"CPurchaseManager.RejectPurchase: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.Assert(a_oID.ExIsValid());

		this.ConfirmPurchase(a_oID, (a_oSender, a_oProductID, a_bIsSuccess) =>
		{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
			var oProduct = this.GetProduct(a_oID);
			bool bIsEnableReject = this.IsInit && (oProduct != null && oProduct.availableToPurchase);

			// 결제 거부가 불가능 할 경우
			if(!bIsEnableReject)
			{
				goto PURCHASE_MANAGER_REJECT_PURCHASE_EXIT;
			}

PURCHASE_MANAGER_REJECT_PURCHASE_EXIT:
			this.HandlePurchaseResult(a_oID, bIsEnableReject, false, true);
			CFunc.Invoke(ref a_oCallback, this, a_oID, bIsEnableReject);
#else
			CFunc.Invoke(ref a_oCallback, this, a_oID, false);
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID			
		});
	}

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	/** 상품이 복원되었을 경우 */
	private void OnRestoreProducts(bool a_bIsSuccess)
	{
		CFunc.ShowLog($"CPurchaseManager.OnRestoreProducts: {a_bIsSuccess}", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_RESTORE_PRODUCTS_CALLBACK, () =>
		{
			var oProductList = new List<Product>();

			// 상품 복원에 실패했을 경우
			if(!a_bIsSuccess)
			{
				goto PURCHASE_MANAGER_ON_RESTORE_PRODUCTS_EXIT;
			}

			for(int i = 0; i < m_oStoreController.products.all.Length; ++i)
			{
				// 결제 된 비소모 상품 일 경우
				if(this.IsPurchaseNonConsumableProduct(m_oStoreController.products.all[i]))
				{
					oProductList.ExAddVal(m_oStoreController.products.all[i]);
				}

				this.RemovePurchaseProductID(m_oStoreController.products.all[i].definition.id);
			}

PURCHASE_MANAGER_ON_RESTORE_PRODUCTS_EXIT:
			m_oCallbackDictB.GetValueOrDefault(EPurchaseCallback.RESTORE)?.Invoke(this,
				oProductList, a_bIsSuccess && oProductList.ExIsValid());
		});
	}

	/** 결제 결과를 처리한다 */
	private void HandlePurchaseResult(string a_oProductID,
		bool a_bIsSuccess, bool a_bIsInvoke = true, bool a_bIsComplete = false)
	{
		CFunc.ShowLog($"CPurchaseManager.HandlePurchaseResult: {a_oProductID}, {a_bIsSuccess}, {a_bIsInvoke}, {a_bIsComplete}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.Assert(a_oProductID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_HANDLE_PURCHASE_RESULT_CALLBACK, () =>
		{
			// 완료 모드 일 경우
			if(a_bIsComplete)
			{
				m_bIsPurchasing = false;
				this.RemovePurchaseProductID(a_oProductID);
			}

			// 콜백 호출 모드 일 경우
			if(a_bIsInvoke)
			{
				var oCallback = m_oCallbackDictA.GetValueOrDefault(EPurchaseCallback.PURCHASE);
				oCallback?.Invoke(this, a_oProductID, a_bIsSuccess);
			}
		});
	}
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 함수
}
#endif // #if PURCHASE_MODULE_ENABLE
