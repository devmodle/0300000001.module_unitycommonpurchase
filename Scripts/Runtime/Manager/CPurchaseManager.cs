using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if PURCHASE_MODULE_ENABLE
using UnityEngine.Purchasing;

#if RECEIPT_CHECK_ENABLE
using UnityEngine.Purchasing.Security;
#endif			// #if RECEIPT_CHECK_ENABLE

#if MSG_PACK_ENABLE
using MessagePack;
#else
[System.Obsolete(KCDefine.U_MSG_NEED_MSG_PACK, true)]
#endif			// #if MSG_PACK_ENABLE

//! 인앱 결제 관리자
public class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener {
	#region 변수
	private bool m_bIsPurchasing = false;

	private IStoreController m_oStoreController = null;
	private IExtensionProvider m_oExtensionProvider = null;

	private System.Action<CPurchaseManager, bool> m_oInitCallback = null;
	private System.Action<CPurchaseManager, List<Product>, bool> m_oRestoreCallback = null;

	private Dictionary<string, System.Action<CPurchaseManager, string, bool>> m_oPurchaseCallbackList = new Dictionary<string, System.Action<CPurchaseManager, string, bool>>();
	#endregion			// 변수

	#region 프로퍼티
	public List<string> PurchaseProductIDList { get; private set; } = new List<string>();
	public bool IsInit => m_oStoreController != null && m_oExtensionProvider != null;
	#endregion			// 프로퍼티

	#region 인터페이스
	//! 초기화 되었을 경우
	public void OnInitialized(IStoreController a_oController, IExtensionProvider a_oProvider) {
		CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

		m_oStoreController = a_oController;
		m_oExtensionProvider = a_oProvider;

		m_oInitCallback?.Invoke(this, true);
	}

	//! 초기화에 실패했을 경우
	public void OnInitializeFailed(InitializationFailureReason a_eReason) {
		CFunc.ShowLogWarning("CPurchaseManager.OnInitializeFailed: {0}", a_eReason);
		m_oInitCallback?.Invoke(this, false);
	}

	//! 결제를 진행 중 일 경우
	public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs a_oArgs) {
		string oID = a_oArgs.purchasedProduct.definition.id;
		CFunc.ShowLog("CPurchaseManager.ProcessPurchase: {0}", KCDefine.B_LOG_COLOR_PLUGIN, a_oArgs.purchasedProduct.definition.id);

		try {
			// 결제 중 일 경우
			if(m_bIsPurchasing) {
				this.PurchaseProductIDList.ExAddValue(oID);

#if MSG_PACK_ENABLE
				this.SavePurchaseProductIDs();
#endif			// #if MSG_PACK_ENABLE
			}

			// 모바일 플랫폼이 아닐 경우
			if(!CAccess.IsMobilePlatform()) {
				this.HandlePurchaseResult(oID, true, true);
				return PurchaseProcessingResult.Pending;
			}

#if RECEIPT_CHECK_ENABLE
			var oValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
			var oReceipts = oValidator.Validate(a_oArgs.purchasedProduct.receipt);
			
			// 영수증이 유효 할 경우
			if(oReceipts.ExIsValid()) {
				this.HandlePurchaseResult(oID, true, true);
			} else {
				this.HandlePurchaseResult(oID, false, true, true);
			}

			return oReceipts.ExIsValid() ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#else
			this.HandlePurchaseResult(oID, true, true);
			return PurchaseProcessingResult.Pending;
#endif			// #if RECEIPT_CHECK_ENABLE
		} catch(System.Exception oException) {
			CFunc.ShowLogWarning("CPurchaseManager.ProcessPurchase Exception: {0}", oException.Message);
			this.PurchaseProductIDList.ExRemoveValue(oID);

#if MSG_PACK_ENABLE
			this.SavePurchaseProductIDs();
#endif			// #if MSG_PACK_ENABLE

			this.HandlePurchaseResult(oID, false, true, true);
		}

		return PurchaseProcessingResult.Complete;
	}

	//! 결제에 실패했을 경우
	public void OnPurchaseFailed(Product a_oProduct, PurchaseFailureReason a_eReason) {
		string oID = a_oProduct.definition.id;
		CFunc.ShowLogWarning("CPurchaseManager.OnPurchaseFailed: {0}, {1}", oID, a_eReason);

		// 중복 결제 상품 일 경우
		if(a_eReason == PurchaseFailureReason.DuplicateTransaction) {
			this.HandlePurchaseResult(oID, true, true);
		} else {
			this.HandlePurchaseResult(oID, false, true, true);
		}
	}
	#endregion			// 인터페이스

	#region 함수
	//! 초기화
	public override void Awake() {
		base.Awake();

#if MSG_PACK_ENABLE
		this.LoadPurchaseProductIDs();
#endif			// #if MSG_PACK_ENABLE
	}

	//! 초기화
	public virtual void Init(List<STProductInfo> a_oProductInfoList, System.Action<CPurchaseManager, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.Init: {0}", KCDefine.B_LOG_COLOR_PLUGIN, a_oProductInfoList);

		// 초기화가 필요 없을 경우
		if(this.IsInit || (!CAccess.IsEditorPlatform() && !CAccess.IsMobilePlatform())) {
			a_oCallback?.Invoke(this, this.IsInit);
		} else {
			m_oInitCallback = a_oCallback;
			var oProductDefinitionList = new List<ProductDefinition>();

			for(int i = 0; i < a_oProductInfoList.Count; ++i) {
				var stProductInfo = a_oProductInfoList[i];
				oProductDefinitionList.Add(new ProductDefinition(stProductInfo.m_oID, stProductInfo.m_eProductType));
			}
			
			var oBuilder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
			oBuilder.AddProducts(oProductDefinitionList);

			UnityPurchasing.Initialize(this, oBuilder);
		}
	}

	//! 상품이 복원 되었을 경우
	public void OnRestoreProducts(bool a_bIsSuccess) {
		CScheduleManager.Instance.AddCallback(KCDefine.U_KEY_PURCHASE_M_RESTORE_CALLBACK, () => {
			CFunc.ShowLog("CPurchaseManager.OnRestoreProducts: {0}", KCDefine.B_LOG_COLOR_PLUGIN, a_bIsSuccess);
			m_bIsPurchasing = false;

			// 실패했을 경우
			if(!a_bIsSuccess) {
				m_oRestoreCallback?.Invoke(this, null, false);
			} else {
				var oProducts = m_oStoreController.products.all;
				var oProductList = new List<Product>();

				for(int i = 0; i < oProducts.Length; ++i) {
					// 결제 된 비 소모품 일 경우
					if(this.IsPurchaseNonConsumableProduct(oProducts[i])) {
						oProductList.ExAddValue(oProducts[i]);
					}

					this.PurchaseProductIDList.ExRemoveValue(oProducts[i].definition.id);
				}

#if MSG_PACK_ENABLE
				this.SavePurchaseProductIDs();
#endif			// #if MSG_PACK_ENABLE

				m_oRestoreCallback?.Invoke(this, oProductList, true);
			}
		});
	}

	//! 비소모 상품 결제 여부를 검사한다
	public bool IsPurchaseNonConsumableProduct(string a_oID) {
		var oProduct = this.GetProduct(a_oID);
		return this.IsPurchaseNonConsumableProduct(oProduct);
	}

	//! 비소모 상품 결제 여부를 검사한다
	public bool IsPurchaseNonConsumableProduct(Product a_oProduct) {
		return a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable;
	}

	//! 상품을 반환한다
	public Product GetProduct(string a_oID) {
		return this.IsInit ? m_oStoreController.products.WithID(a_oID) : null;
	}

	//! 상품을 결제한다
	public void PurchaseProduct(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.PurchaseProduct: {0}", KCDefine.B_LOG_COLOR_PLUGIN, a_oID);
		
		var oProduct = this.GetProduct(a_oID);
		bool bIsEnablePurchase = oProduct != null && oProduct.availableToPurchase;

		// 결제가 불가능 할 경우
		if(!this.IsInit || !bIsEnablePurchase || m_oPurchaseCallbackList.ContainsKey(a_oID)) {
			a_oCallback?.Invoke(this, a_oID, false);
		} else {
			m_bIsPurchasing = true;
			m_oPurchaseCallbackList.Add(a_oID, a_oCallback);

			// 결제 된 상품 일 경우
			if(this.PurchaseProductIDList.Contains(a_oID) || this.IsPurchaseNonConsumableProduct(oProduct)) {
				this.HandlePurchaseResult(a_oID, true, true);
			} else {
				m_oStoreController.InitiatePurchase(a_oID);
			}
		}
	}

	//! 상품을 복구한다
	public void RestoreProducts(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.RestoreProduct", KCDefine.B_LOG_COLOR_PLUGIN);

		// 초기화가 필요 할 경우
		if(!this.IsInit) {
			a_oCallback?.Invoke(this, null, false);
		} else {
#if UNITY_IOS
			var oStoreExtension = m_oExtensionProvider.GetExtension<IAppleExtensions>();
#else
			var oStoreExtension = m_oExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();
#endif			// #if UNITY_IOS

			m_bIsPurchasing = CAccess.IsMobilePlatform();
			m_oRestoreCallback = a_oCallback;

			// 모바일 플랫폼이 아닐 경우
			if(!CAccess.IsMobilePlatform()) {
				this.OnRestoreProducts(true);
			} else {
				oStoreExtension.RestoreTransactions(this.OnRestoreProducts);
			}
		}
	}

	//! 결제를 확정한다
	public void ConfirmPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CScheduleManager.Instance.AddCallback(KCDefine.U_KEY_PURCHASE_M_CONFIRM_CALLBACK, () => {
			CFunc.ShowLog("CPurchaseManager.ConfirmPurchase: {0}", KCDefine.B_LOG_COLOR_PLUGIN, a_oID);

			var oProduct = this.GetProduct(a_oID);
			bool bIsEnablePurchase = oProduct != null && oProduct.availableToPurchase;

			// 결제 확정이 불가능 할 경우
			if(!this.IsInit || !bIsEnablePurchase || !m_oPurchaseCallbackList.ContainsKey(a_oID)) {
				a_oCallback?.Invoke(this, a_oID, false);
			} else {
				m_oStoreController.ConfirmPendingPurchase(oProduct);
				this.PurchaseProductIDList.ExRemoveValue(a_oID);
				
#if MSG_PACK_ENABLE
				this.SavePurchaseProductIDs();
#endif			// #if MSG_PACK_ENABLE

				this.HandlePurchaseResult(a_oID, true, false, true);

				m_bIsPurchasing = false;
				a_oCallback?.Invoke(this, a_oID, true);
			}
		});
	}

	//! 결제 결과를 처리한다
	private void HandlePurchaseResult(string a_oID, bool a_bIsSuccess, bool a_bIsInvokeCallback = true, bool a_bIsRemoveCallback = false) {
		CScheduleManager.Instance.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_RESULT_CALLBACK, () => {
			CFunc.ShowLog("CPurchaseManager.HandlePurchaseResult: {0}, {1}, {2}, {3}",
				KCDefine.B_LOG_COLOR_PLUGIN, a_oID, a_bIsSuccess, a_bIsInvokeCallback, a_bIsRemoveCallback);

			// 결제 콜백이 존재 할 경우
			if(m_oPurchaseCallbackList.ContainsKey(a_oID)) {
				var oCallback = m_oPurchaseCallbackList[a_oID];

				// 제거 모드 일 경우
				if(a_bIsRemoveCallback) {
					m_oPurchaseCallbackList.Remove(a_oID);
				}

				// 호출 모드 일 경우
				if(a_bIsInvokeCallback) {
					oCallback?.Invoke(this, a_oID, a_bIsSuccess);
				}
			}
		});
	}
	#endregion			// 함수

	#region 조건부 함수
#if MSG_PACK_ENABLE
	//! 결제 상품 아이디를 저장한다
	private void SavePurchaseProductIDs() {
		CFunc.ShowLog("CPurchaseManager.SavePurchaseProductIDs: {0}, {1}", 
			KCDefine.B_LOG_COLOR_PLUGIN, this.PurchaseProductIDList, this.PurchaseProductIDList.Count);

		var oBytes = MessagePackSerializer.Serialize<List<string>>(this.PurchaseProductIDList);

#if SECURITY_ENABLE
		CFunc.WriteSecurityBytes(KCDefine.U_DATA_PATH_PURCHASE_M_PRODUCT_ID_LIST, oBytes);
#else
		CFunc.WriteBytes(KCDefine.U_DATA_PATH_PURCHASE_M_PRODUCT_ID_LIST, oBytes);
#endif			// #if SECURITY_ENABLE
	}

	//! 결제 상품 아이디를 로드한다
	private void LoadPurchaseProductIDs() {
		CFunc.ShowLog("CPurchaseManager.LoadPurchaseProductIDs", KCDefine.B_LOG_COLOR_PLUGIN);

		// 파일이 존재 할 경우
		if(File.Exists(KCDefine.U_DATA_PATH_PURCHASE_M_PRODUCT_ID_LIST)) {
#if SECURITY_ENABLE
			var oBytes = CFunc.ReadSecurityBytes(KCDefine.U_DATA_PATH_PURCHASE_M_PRODUCT_ID_LIST);
#else
			var oBytes = CAccess.ReadBytes(KCDefine.PATH_PURCHASE_M_PRODUCT_ID_LIST);
#endif			// #if SECURITY_ENABLE

			this.PurchaseProductIDList = MessagePackSerializer.Deserialize<List<string>>(oBytes);
			
			CFunc.ShowLog("CPurchaseManager.OnLoadPurchaseProductIDs: {0}, {1}", 
				KCDefine.B_LOG_COLOR_PLUGIN, this.PurchaseProductIDList, this.PurchaseProductIDList.Count);
		}
	}
#endif			// #if MSG_PACK_ENABLE
	#endregion			// 조건부 함수
}
#endif			// #if PURCHASE_MODULE_ENABLE
