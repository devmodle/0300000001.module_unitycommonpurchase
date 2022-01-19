using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

#if PURCHASE_MODULE_ENABLE
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Purchasing;
#endif			// #if UNITY_EDITOR

#if (UNITY_IOS || UNITY_ANDROID) && RECEIPT_CHECK_ENABLE
using UnityEngine.Purchasing.Security;
#endif			// #if (UNITY_IOS || UNITY_ANDROID) && RECEIPT_CHECK_ENABLE

/** 인앱 결제 관리자 */
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener {
	/** 매개 변수 */
	public struct STParams {
		public List<STProductInfo> m_oProductInfoList;
	}

	/** 콜백 매개 변수 */
	public struct STCallbackParams {
		public System.Action<CPurchaseManager, bool> m_oCallback;
	}

	#region 변수
	private STParams m_stParams;
	private STCallbackParams m_stCallbackParams;

	private List<string> m_oPurchaseProductIDList = new List<string>();

	private System.Action<CPurchaseManager, string, bool> m_oPurchaseCallback = null;
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

	public bool IsPurchasing => m_oPurchaseCallback != null && m_oRestoreCallback != null;
	#endregion			// 프로퍼티

	#region IStoreListener
	/** 초기화 되었을 경우 */
	public virtual void OnInitialized(IStoreController a_oController, IExtensionProvider a_oProvider) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_CALLBACK, () => {
			m_oStoreController = a_oController;
			m_oExtensionProvider = a_oProvider;

			CFunc.Invoke(ref m_stCallbackParams.m_oCallback, this, this.IsInit);
		});
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CFunc.ShowLogWarning($"CPurchaseManager.OnInitializeFailed: {a_eReason}");
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_FAIL_CALLBACK, () => CFunc.Invoke(ref m_stCallbackParams.m_oCallback, this, false));
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 결제를 진행 중 일 경우 */
	public virtual PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs a_oArgs) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		string oID = a_oArgs.purchasedProduct.definition.id;
		CFunc.ShowLog($"CPurchaseManager.ProcessPurchase: {oID}", KCDefine.B_LOG_COLOR_PLUGIN);

		try {
			// 결제 중 일 경우
			if(this.IsPurchasing) {
				this.AddPurchaseProductID(oID);
			}

#if !UNITY_EDITOR && ((UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)) && RECEIPT_CHECK_ENABLE)
			var oValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
			var oReceipts = oValidator.Validate(a_oArgs.purchasedProduct.receipt);

			// 영수증이 유효 할 경우
			if(oReceipts.ExIsValid()) {
				this.HandlePurchaseResult(oID, true, true);
			} else {
				this.HandlePurchaseResult(oID, false, true, true);
			}

			return (this.IsPurchasing && oReceipts.ExIsValid()) ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#else
			this.HandlePurchaseResult(oID, true, true);
			return this.IsPurchasing ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#endif			// #if !UNITY_EDITOR && ((UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)) && RECEIPT_CHECK_ENABLE)
		} catch(System.Exception oException) {
			CFunc.ShowLogWarning($"CPurchaseManager.ProcessPurchase Exception: {oException.Message}");

			this.RemovePurchaseProductID(oID);
			this.HandlePurchaseResult(oID, false, true, true);
		}
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)

		return PurchaseProcessingResult.Complete;
	}

	/** 결제에 실패했을 경우 */
	public virtual void OnPurchaseFailed(Product a_oProduct, PurchaseFailureReason a_eReason) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_FAIL_CALLBACK, () => {
			string oID = a_oProduct.definition.id;
			CFunc.ShowLogWarning($"CPurchaseManager.OnPurchaseFailed: {oID}, {a_eReason}");

			// 중복 결제 상품 일 경우
			if(this.IsPurchaseNonConsumableProduct(a_oProduct) || a_eReason == PurchaseFailureReason.DuplicateTransaction) {
				this.HandlePurchaseResult(oID, true, true);
			} else {
				this.HandlePurchaseResult(oID, false, true, true);
			}
		});
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	#endregion			// IStoreListener

	#region 함수
	/** 초기화 */
	public override void Awake() {
		base.Awake();

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 결제 상품 식별자 파일이 존재 할 경우
		if(File.Exists(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS)) {
			m_oPurchaseProductIDList = CFunc.ReadMsgPackObj<List<string>>(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS);
		}
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 초기화 */
	public virtual void Init(STParams a_stParams, STCallbackParams a_stCallbackParams) {
		CFunc.ShowLog($"CPurchaseManager.Init: {a_stParams.m_oProductInfoList}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_stParams.m_oProductInfoList != null);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 초기화 되었을 경우
		if(this.IsInit) {
			a_stCallbackParams.m_oCallback?.Invoke(this, true);
		} else {
			m_stParams = a_stParams;
			m_stCallbackParams = a_stCallbackParams;

			var oProductDefinitionList = new List<ProductDefinition>();

			for(int i = 0; i < a_stParams.m_oProductInfoList.Count; ++i) {
				CAccess.Assert(a_stParams.m_oProductInfoList[i].m_oID.ExIsValid() && a_stParams.m_oProductInfoList[i].m_eProductType != ProductType.Subscription);
				oProductDefinitionList.ExAddVal(new ProductDefinition(a_stParams.m_oProductInfoList[i].m_oID, a_stParams.m_oProductInfoList[i].m_eProductType));
			}
			
			var oBuilder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
			oBuilder.AddProducts(oProductDefinitionList);

			UnityPurchasing.Initialize(this, oBuilder);
		}
#else
		a_stCallbackParams.m_oCallback?.Invoke(this, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 비소모 상품 결제 여부를 검사한다 */
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

	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseNonConsumableProduct(Product a_oProduct) {
		CAccess.Assert(a_oProduct != null);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit && (a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable);
#else
		return false;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 상품을 반환한다 */
	public Product GetProduct(string a_oID) {
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit ? m_oStoreController.products.WithID(a_oID) : null;
#else
		return null;
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 상품을 결제한다 */
	public void PurchaseProduct(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog($"CPurchaseManager.PurchaseProduct: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		var oProduct = this.GetProduct(a_oID);
		bool bIsEnablePurchase = this.IsInit && (oProduct != null && oProduct.availableToPurchase);

		// 결제 가능 할 경우
		if(bIsEnablePurchase && !this.IsPurchasing) {
			m_oPurchaseCallback = a_oCallback;

			// 결제 된 상품 일 경우
			if(m_oPurchaseProductIDList.Contains(a_oID) || this.IsPurchaseNonConsumableProduct(oProduct)) {
				this.HandlePurchaseResult(a_oID, true, true);
			} else {
				m_oStoreController.InitiatePurchase(oProduct, KCDefine.U_PAYLOAD_PURCHASE_M_PURCHASE);
			}
		} else {
			a_oCallback?.Invoke(this, a_oID, false);
		}
#else
		a_oCallback?.Invoke(this, a_oID, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	
	/** 상품을 복원한다 */
	public void RestoreProducts(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.RestoreProducts", KCDefine.B_LOG_COLOR_PLUGIN);

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
		// 초기화 되었을 경우
		if(this.IsInit && !this.IsPurchasing) {
			m_oRestoreCallback = a_oCallback;

#if UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
#if UNITY_IOS
			var oStoreExtension = m_oExtensionProvider.GetExtension<IAppleExtensions>();
#else
			var oStoreExtension = m_oExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();
#endif			// #if UNITY_IOS

			oStoreExtension.RestoreTransactions(this.OnRestoreProducts);
#else
			this.OnRestoreProducts(true);
#endif			// #if UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
		} else {
			a_oCallback?.Invoke(this, null, false);
		}
#else
		a_oCallback?.Invoke(this, null, false);
#endif			// #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	}

	/** 결제를 확정한다 */
	public void ConfirmPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog($"CPurchaseManager.ConfirmPurchase: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_CONFIRM_CALLBACK, () => {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
			var oProduct = this.GetProduct(a_oID);
			bool bIsEnableConfirm = this.IsInit && (oProduct != null && oProduct.availableToPurchase);

			// 확정 가능 할 경우
			if(bIsEnableConfirm && this.IsPurchasing) {
				m_oStoreController.ConfirmPendingPurchase(oProduct);

				this.RemovePurchaseProductID(a_oID);
				this.HandlePurchaseResult(a_oID, true, false, true);

				a_oCallback?.Invoke(this, a_oID, true);
			} else {
				a_oCallback?.Invoke(this, a_oID, false);
			}
#else
			a_oCallback?.Invoke(this, a_oID, false);
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		});
	}
	#endregion			// 함수

	#region 조건부 함수
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	/** 결제 상품 식별자를 추가한다 */
	private void AddPurchaseProductID(string a_oID, bool a_bIsAutoSave = true) {
		m_oPurchaseProductIDList.ExAddVal(a_oID);

		// 자동 저장 모드 일 경우
		if(a_bIsAutoSave) {
			CFunc.WriteMsgPackObj<List<string>>(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS, m_oPurchaseProductIDList);
		}
	}

	/** 결제 상품 식별자를 제거한다 */
	private void RemovePurchaseProductID(string a_oID, bool a_bIsAutoSave = true) {
		m_oPurchaseProductIDList.ExRemoveVal(a_oID);

		// 자동 저장 모드 일 경우
		if(a_bIsAutoSave) {
			CFunc.WriteMsgPackObj<List<string>>(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS, m_oPurchaseProductIDList);
		}
	}

	/** 결제 결과를 처리한다 */
	private void HandlePurchaseResult(string a_oProductID, bool a_bIsSuccess, bool a_bIsInvoke = true, bool a_bIsComplete = false) {
		CFunc.ShowLog($"CPurchaseManager.HandlePurchaseResult: {a_oProductID}, {a_bIsSuccess}, {a_bIsInvoke}, {a_bIsComplete}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oProductID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_RESULT_CALLBACK, () => {
			var oCallback = m_oPurchaseCallback;

			try {
				m_oPurchaseCallback = a_bIsComplete ? null : m_oPurchaseCallback;
			} finally {
				// 호출 모드 일 경우
				if(a_bIsInvoke) {
					oCallback?.Invoke(this, a_oProductID, a_bIsSuccess);
				}
			}
		});
	}
#endif			// #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	/** 상품이 복원 되었을 경우 */
	private void OnRestoreProducts(bool a_bIsSuccess) {
		CFunc.ShowLog($"CPurchaseManager.OnRestoreProducts: {a_bIsSuccess}", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_RESTORE_CALLBACK, () => {
			var oProductList = new List<Product>();

			// 성공했을 경우
			if(a_bIsSuccess) {
				foreach(var oProduct in m_oStoreController.products.set) {
					// 결제 된 비소모 상품 일 경우
					if(this.IsPurchaseNonConsumableProduct(oProduct)) {
						oProductList.ExAddVal(oProduct);
					}

					this.RemovePurchaseProductID(oProduct.definition.id);
				}
			}

			CFunc.Invoke(ref m_oRestoreCallback, this, oProductList, a_bIsSuccess);
		});
	}
#endif			// #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
	#endregion			// 조건부 함수

	#region 조건부 클래스 함수
#if UNITY_EDITOR
	/** 초기화 */
	[InitializeOnLoadMethod]
	public static void EditorInitialize() {
#if UNITY_ANDROID
#if ANDROID_AMAZON_PLATFORM
		UnityPurchasingEditor.TargetAndroidStore(AppStore.AmazonAppStore);
#else
		UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif			// #if ANDROID_AMAZON_PLATFORM
#endif			// #if UNITY_ANDROID
	}
#endif			// #if UNITY_EDITOR
	#endregion			// 조건부 클래스 함수
}
#endif			// #if PURCHASE_MODULE_ENABLE
