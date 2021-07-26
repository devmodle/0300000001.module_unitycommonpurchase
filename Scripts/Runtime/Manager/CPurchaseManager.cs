using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MessagePack;

#if PURCHASE_MODULE_ENABLE
using UnityEngine.Purchasing;

#if RECEIPT_CHECK_ENABLE && (UNITY_IOS || UNITY_ANDROID)
using UnityEngine.Purchasing.Security;
#endif			// #if RECEIPT_CHECK_ENABLE && (UNITY_IOS || UNITY_ANDROID)

//! 인앱 결제 관리자
public class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener {
	//! 매개 변수
	public struct STParams {
		public List<STProductInfo> m_oProductInfoList;
	}

	#region 변수
	private STParams m_stParams;
	
	private bool m_bIsPurchasing = false;
	private HashSet<string> m_oPurchaseProductIDList = new HashSet<string>();
	private Dictionary<string, System.Action<CPurchaseManager, string, bool>> m_oPurchaseCallbackList = new Dictionary<string, System.Action<CPurchaseManager, string, bool>>();

	private System.Action<CPurchaseManager, bool> m_oInitCallback = null;
	private System.Action<CPurchaseManager, List<Product>, bool> m_oRestoreCallback = null;

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	private IStoreController m_oStoreController = null;
	private IExtensionProvider m_oExtensionProvider = null;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	#endregion			// 변수

	#region 프로퍼티
	public bool IsInit {
		get {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
			return m_oStoreController != null && m_oExtensionProvider != null;
#else
			return false;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		}
	}
	#endregion			// 프로퍼티

	#region 인터페이스
	// 초기화 되었을 경우
	public void OnInitialized(IStoreController a_oController, IExtensionProvider a_oProvider) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_CALLBACK, () => {
			CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

			m_oStoreController = a_oController;
			m_oExtensionProvider = a_oProvider;

			CFunc.Invoke(ref m_oInitCallback, this, this.IsInit);
		});
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 초기화에 실패했을 경우
	public void OnInitializeFailed(InitializationFailureReason a_eReason) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_FAIL_CALLBACK, () => {
			CFunc.ShowLogWarning("CPurchaseManager.OnInitializeFailed: {0}", a_eReason);
			CFunc.Invoke(ref m_oInitCallback, this, false);
		});
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 결제를 진행 중 일 경우
	public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs a_oArgs) {
		CFunc.ShowLog($"CPurchaseManager.ProcessPurchase: {a_oArgs.purchasedProduct.definition.id}", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		string oID = a_oArgs.purchasedProduct.definition.id;

		try {
			// 결제 중 일 경우
			if(m_bIsPurchasing) {
				m_oPurchaseProductIDList.Add(oID);
				this.SavePurchaseProductIDs();
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
			m_oPurchaseProductIDList.ExRemoveVal(oID);

			this.SavePurchaseProductIDs();
			this.HandlePurchaseResult(oID, false, true, true);
		}
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)

		return PurchaseProcessingResult.Complete;
	}

	//! 결제에 실패했을 경우
	public void OnPurchaseFailed(Product a_oProduct, PurchaseFailureReason a_eReason) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_FAIL_CALLBACK, () => {
			string oID = a_oProduct.definition.id;
			CFunc.ShowLogWarning("CPurchaseManager.OnPurchaseFailed: {0}, {1}", oID, a_eReason);

			// 중복 결제 상품 일 경우
			if(this.IsPurchaseNonConsumableProduct(a_oProduct) || a_eReason == PurchaseFailureReason.DuplicateTransaction) {
				this.HandlePurchaseResult(oID, true, true);
			} else {
				this.HandlePurchaseResult(oID, false, true, true);
			}
		});
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	#endregion			// 인터페이스

	#region 함수
	//! 초기화
	public override void Awake() {
		base.Awake();

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		this.LoadPurchaseProductIDs();
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 초기화
	public virtual void Init(STParams a_stParams, System.Action<CPurchaseManager, bool> a_oCallback) {
		CAccess.Assert(a_stParams.m_oProductInfoList != null);
		CFunc.ShowLog($"CPurchaseManager.Init: {a_stParams.m_oProductInfoList}", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 초기화 되었을 경우
		if(this.IsInit) {
			a_oCallback?.Invoke(this, true);
		} else {
			m_stParams = a_stParams;
			m_oInitCallback = a_oCallback;

			var oProductDefinitionList = new List<ProductDefinition>();

			for(int i = 0; i < a_stParams.m_oProductInfoList.Count; ++i) {
				CAccess.Assert(a_stParams.m_oProductInfoList[i].m_oID.ExIsValid() && a_stParams.m_oProductInfoList[i].m_eProductType != ProductType.Subscription);

				var oProductDefinition = new ProductDefinition(a_stParams.m_oProductInfoList[i].m_oID, a_stParams.m_oProductInfoList[i].m_eProductType);
				oProductDefinitionList.ExAddVal(oProductDefinition);
			}
			
			var oBuilder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
			oBuilder.AddProducts(oProductDefinitionList);

			UnityPurchasing.Initialize(this, oBuilder);
		}
#else
		a_oCallback?.Invoke(this, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 비소모 상품 결제 여부를 검사한다
	public bool IsPurchaseNonConsumableProduct(string a_oID) {
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 초기화 되었을 경우
		if(this.IsInit) {
			var oProduct = this.GetProduct(a_oID);
			return this.IsPurchaseNonConsumableProduct(oProduct);
		}

		return false;
#else
		return false;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 비소모 상품 결제 여부를 검사한다
	public bool IsPurchaseNonConsumableProduct(Product a_oProduct) {
		CAccess.Assert(a_oProduct != null);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit && (a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable);
#else
		return false;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 상품을 반환한다
	public Product GetProduct(string a_oID) {
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit ? m_oStoreController.products.WithID(a_oID) : null;
#else
		return null;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	//! 상품을 결제한다
	public void PurchaseProduct(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CAccess.Assert(a_oID.ExIsValid());
		CFunc.ShowLog($"CPurchaseManager.PurchaseProduct: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		var oProduct = this.GetProduct(a_oID);
		bool bIsEnablePurchase = this.IsInit && (oProduct != null && oProduct.availableToPurchase);

		// 결제 가능 할 경우
		if(bIsEnablePurchase && !m_oPurchaseCallbackList.ContainsKey(a_oID)) {
			m_bIsPurchasing = true;
			m_oPurchaseCallbackList.Add(a_oID, a_oCallback);

			// 결제 된 상품 일 경우
			if(m_oPurchaseProductIDList.Contains(a_oID) || this.IsPurchaseNonConsumableProduct(oProduct)) {
				this.HandlePurchaseResult(a_oID, true, true);
			} else {
				m_oStoreController.InitiatePurchase(a_oID);
			}
		} else {
			a_oCallback?.Invoke(this, a_oID, false);
		}
#else
		a_oCallback?.Invoke(this, a_oID, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	
	//! 상품을 복원한다
	public void RestoreProducts(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.RestoreProducts", KCDefine.B_LOG_COLOR_PLUGIN);

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
		// 초기화 되었을 경우
		if(this.IsInit) {
#if UNITY_IOS
			var oStoreExtension = m_oExtensionProvider.GetExtension<IAppleExtensions>();
#else
			var oStoreExtension = m_oExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();
#endif			// #if UNITY_IOS

			m_bIsPurchasing = true;
			m_oRestoreCallback = a_oCallback;

			oStoreExtension.RestoreTransactions(this.OnRestoreProducts);
		} else {
			a_oCallback?.Invoke(this, null, false);
		}
#else
		a_oCallback?.Invoke(this, null, false);
#endif			// #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	}

	//! 결제를 확정한다
	public void ConfirmPurchase(string a_oProductID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CAccess.Assert(a_oProductID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_CONFIRM_CALLBACK, () => {
			CFunc.ShowLog($"CPurchaseManager.ConfirmPurchase: {a_oProductID}", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
			var oProduct = this.GetProduct(a_oProductID);
			bool bIsEnablePurchase = this.IsInit && (oProduct != null && oProduct.availableToPurchase);

			// 확정 가능 할 경우
			if(bIsEnablePurchase && m_oPurchaseCallbackList.ContainsKey(a_oProductID)) {
				m_oStoreController.ConfirmPendingPurchase(oProduct);
				m_oPurchaseProductIDList.ExRemoveVal(a_oProductID);
				
				this.SavePurchaseProductIDs();
				this.HandlePurchaseResult(a_oProductID, true, false, true);

				m_bIsPurchasing = false;
				a_oCallback?.Invoke(this, a_oProductID, true);
			} else {
				a_oCallback?.Invoke(this, a_oProductID, false);
			}
#else
			a_oCallback?.Invoke(this, a_oProductID, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		});
	}
	#endregion			// 함수

	#region 조건부 함수
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	//! 결제 상품 식별자를 저장한다
	private void SavePurchaseProductIDs() {
		CFunc.ShowLog($"CPurchaseManager.SavePurchaseProductIDs: {m_oPurchaseProductIDList}, {m_oPurchaseProductIDList.Count}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.WriteMsgPackObj<HashSet<string>>(KCDefine.U_DATA_P_PURCHASE_M_PRODUCT_ID_LIST, m_oPurchaseProductIDList);
	}

	//! 결제 상품 식별자를 로드한다
	private void LoadPurchaseProductIDs() {
		CFunc.ShowLog("CPurchaseManager.LoadPurchaseProductIDs", KCDefine.B_LOG_COLOR_PLUGIN);

		// 파일이 존재 할 경우
		if(File.Exists(KCDefine.U_DATA_P_PURCHASE_M_PRODUCT_ID_LIST)) {
			m_oPurchaseProductIDList = CFunc.ReadMsgPackObj<HashSet<string>>(KCDefine.U_DATA_P_PURCHASE_M_PRODUCT_ID_LIST);
			CFunc.ShowLog($"CPurchaseManager.OnLoadPurchaseProductIDs: {m_oPurchaseProductIDList}, {m_oPurchaseProductIDList.Count}", KCDefine.B_LOG_COLOR_PLUGIN);
		}
	}

	//! 결제 결과를 처리한다
	private void HandlePurchaseResult(string a_oProductID, bool a_bIsSuccess, bool a_bIsInvokeCallback = true, bool a_bIsRemoveCallback = false) {
		CAccess.Assert(a_oProductID.ExIsValid());
		
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_RESULT_CALLBACK, () => {
			CFunc.ShowLog($"CPurchaseManager.HandlePurchaseResult: {a_oProductID}, {a_bIsSuccess}, {a_bIsInvokeCallback}, {a_bIsRemoveCallback}", KCDefine.B_LOG_COLOR_PLUGIN);

			// 결제 콜백이 존재 할 경우
			if(m_oPurchaseCallbackList.TryGetValue(a_oProductID, out System.Action<CPurchaseManager, string, bool> oCallback)) {
				// 제거 모드 일 경우
				if(a_bIsRemoveCallback) {
					m_oPurchaseCallbackList.ExRemoveVal(a_oProductID);
				}

				// 호출 모드 일 경우
				if(a_bIsInvokeCallback) {
					oCallback?.Invoke(this, a_oProductID, a_bIsSuccess);
				}
			}
		});
	}
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	//! 상품이 복원 되었을 경우
	private void OnRestoreProducts(bool a_bIsSuccess) {
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_RESTORE_CALLBACK, () => {
			CFunc.ShowLog($"CPurchaseManager.OnRestoreProducts: {a_bIsSuccess}", KCDefine.B_LOG_COLOR_PLUGIN);
			m_bIsPurchasing = false;

			// 실패했을 경우
			if(!a_bIsSuccess) {
				m_oRestoreCallback?.Invoke(this, null, false);
			} else {
				var oProducts = m_oStoreController.products.all;
				var oProductList = new List<Product>();

				for(int i = 0; i < oProducts.Length; ++i) {
					// 결제 된 비소모 상품 일 경우
					if(this.IsPurchaseNonConsumableProduct(oProducts[i])) {
						oProductList.ExAddVal(oProducts[i]);
					}

					m_oPurchaseProductIDList.ExRemoveVal(oProducts[i].definition.id);
				}
				
				this.SavePurchaseProductIDs();
				m_oRestoreCallback?.Invoke(this, oProductList, true);
			}

			m_oRestoreCallback = null;
		});
	}
#endif			// #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	#endregion			// 조건부 함수
}
#endif			// #if PURCHASE_MODULE_ENABLE
