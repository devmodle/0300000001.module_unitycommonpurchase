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

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Purchasing;
#endif // #if UNITY_EDITOR

#if(UNITY_IOS || UNITY_ANDROID) && RECEIPT_CHECK_ENABLE
using UnityEngine.Purchasing.Security;
#endif // #if (UNITY_IOS || UNITY_ANDROID) && RECEIPT_CHECK_ENABLE

/** 인앱 결제 관리자 */
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener, IDetailedStoreListener {
	/** 콜백 */
	public enum ECallback {
		NONE = -1,
		INIT,
		[HideInInspector] MAX_VAL
	}

	/** 결제 콜백 */
	private enum EPurchaseCallback {
		NONE = -1,
		RESTORE,
		PURCHASE,
		[HideInInspector] MAX_VAL
	}

	/** 매개 변수 */
	public struct STParams {
		public List<STProductInfo> m_oProductInfoList;
		public Dictionary<ECallback, System.Action<CPurchaseManager, bool>> m_oCallbackDict;
	}

	#region 변수
	private bool m_bIsPurchasing = false;
	private List<string> m_oPurchaseProductIDList = new List<string>();
	private Dictionary<EPurchaseCallback, System.Action<CPurchaseManager, string, bool>> m_oCallbackDictA = new Dictionary<EPurchaseCallback, System.Action<CPurchaseManager, string, bool>>();
	private Dictionary<EPurchaseCallback, System.Action<CPurchaseManager, List<Product>, bool>> m_oCallbackDictB = new Dictionary<EPurchaseCallback, System.Action<CPurchaseManager, List<Product>, bool>>();

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	private IStoreController m_oStoreController = null;
	private IExtensionProvider m_oExtensionProvider = null;
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	#endregion // 변수

	#region 프로퍼티
	public STParams Params { get; private set; }
	public bool IsInit { get; private set; } = false;
	#endregion // 프로퍼티

	#region IStoreListener
	/** 초기화되었을 경우 */
	public virtual void OnInitialized(IStoreController a_oController, IExtensionProvider a_oProvider) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_CALLBACK, () => {
			m_oStoreController = a_oController;
			m_oExtensionProvider = a_oProvider;

#if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)
			StandardPurchasingModule.Instance().useFakeStoreAlways = true;
			StandardPurchasingModule.Instance().useFakeStoreUIMode = FakeStoreUIMode.Default;
#endif // #if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)

			this.IsInit = true;
			this.Params.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, this.IsInit);
		});
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason) {
		this.OnInitializeFailed(a_eReason, string.Empty);
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason, string? a_oMsg) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CFunc.ShowLogWarning($"CPurchaseManager.OnInitializeFailed: {a_eReason}, {a_oMsg}");

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_INIT_FAIL_CALLBACK, () => {
			this.Params.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, false);
		});
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 결제를 진행 중 일 경우 */
	public virtual PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs a_oArgs) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		string oID = a_oArgs.purchasedProduct.definition.id;
		CFunc.ShowLog($"CPurchaseManager.ProcessPurchase: {oID}", KCDefine.B_LOG_COLOR_PLUGIN);

		try {
			// 결제 중 일 경우
			if(m_bIsPurchasing) {
				this.AddPurchaseProductID(oID);
			}

#if !UNITY_EDITOR && ((UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)) && RECEIPT_CHECK_ENABLE)
			var oValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
			var oPurchaseReceipts = oValidator.Validate(a_oArgs.purchasedProduct.receipt);

			// 결제 영수증이 유효 할 경우
			if(oPurchaseReceipts.ExIsValid()) {
				this.HandlePurchaseResult(oID, true);
			} else {
				this.HandlePurchaseResult(oID, false, a_bIsComplete: true);
			}

			return (m_bIsPurchasing && oPurchaseReceipts.ExIsValid()) ? 
				PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#else
			this.HandlePurchaseResult(oID, true);
			return m_bIsPurchasing ? PurchaseProcessingResult.Pending : PurchaseProcessingResult.Complete;
#endif // #if !UNITY_EDITOR && ((UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)) && RECEIPT_CHECK_ENABLE)
		} catch(System.Exception oException) {
			CFunc.ShowLogWarning($"CPurchaseManager.ProcessPurchase Exception: {oException.Message}");
			this.HandlePurchaseResult(oID, false, a_bIsComplete: true);
		}
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)

		return PurchaseProcessingResult.Complete;
	}

	/** 결제에 실패했을 경우 */
	public virtual void OnPurchaseFailed(Product a_oProduct, PurchaseFailureReason a_eReason) {
		var oDesc = new PurchaseFailureDescription(a_oProduct.definition.id, a_eReason, $"{a_eReason}");
		this.OnPurchaseFailed(a_oProduct, oDesc);
	}

	/** 결제에 실패했을 경우 */
	public virtual void OnPurchaseFailed(Product a_oProduct, PurchaseFailureDescription a_oDesc) {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		CFunc.ShowLogWarning($"CPurchaseManager.OnPurchaseFailed: {a_oProduct.definition.id}, {a_oDesc.reason}, {a_oDesc.message}");

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_PURCHASE_FAIL_CALLBACK, () => {
			bool bIsPurchaseNonConsumableProductA = this.IsPurchaseNonConsumableProduct(a_oProduct);
			bool bIsPurchaseNonConsumableProductB = a_oDesc.reason == PurchaseFailureReason.DuplicateTransaction;

			bool bIsPurchaseNonConsumableProduct = bIsPurchaseNonConsumableProductA || bIsPurchaseNonConsumableProductB;

			this.HandlePurchaseResult(a_oProduct.definition.id, 
				bIsPurchaseNonConsumableProduct, a_bIsComplete: !bIsPurchaseNonConsumableProduct);
		});
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	#endregion // IStoreListener

	#region 함수
	/** 초기화 */
	public override void Awake() {
		base.Awake();

#if(UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID))
		// 결제 상품 식별자 파일이 존재 할 경우
		if(File.Exists(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS)) {
			this.LoadPurchaseProductIDs().ExCopyTo(m_oPurchaseProductIDList, (a_oProductID) => a_oProductID);
		}
#endif // #if (UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID))
	}

	/** 초기화 */
	public virtual void Init(STParams a_stParams) {
		CFunc.ShowLog($"CPurchaseManager.Init: {a_stParams.m_oProductInfoList}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_stParams.m_oProductInfoList != null);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 초기화되었을 경우
		if(this.IsInit) {
			a_stParams.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, this.IsInit);
		} else {
			this.Params = a_stParams;

#if DEBUG || DEVELOPMENT_BUILD
			string oEnvironmentName = KCDefine.U_ENVIRONMENT_N_DEV;
#else
			string oEnvironmentName = KCDefine.U_ENVIRONMENT_N_PRODUCTION;
#endif // #if DEBUG || DEVELOPMENT_BUILD

			var oInitOpts = new InitializationOptions();
			var oUnityServices = UnityServices.InitializeAsync(oInitOpts.SetEnvironmentName(oEnvironmentName));

			CTaskManager.Inst.WaitAsyncTask(oUnityServices, this.OnInit);
		}
#else
		a_stParams.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, false);
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseNonConsumableProduct(string a_oID) {
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		var oProduct = this.GetProduct(a_oID);
		return this.IsInit ? this.IsPurchaseNonConsumableProduct(oProduct) : false;
#else
		return false;
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseNonConsumableProduct(Product a_oProduct) {
		CAccess.Assert(a_oProduct != null);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit && a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable;
#else
		return false;
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 상품을 복원한다 */
	public void RestoreProducts(System.Action<CPurchaseManager, List<Product>, bool> a_oCallback) {
		CFunc.ShowLog("CPurchaseManager.RestoreProducts", KCDefine.B_LOG_COLOR_PLUGIN);

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		// 초기화되었을 경우
		if(this.IsInit && !m_bIsPurchasing) {
			m_oCallbackDictB.ExReplaceVal(EPurchaseCallback.RESTORE, a_oCallback);

#if UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
#if UNITY_IOS
			var oStoreExtension = m_oExtensionProvider.GetExtension<IAppleExtensions>();
#else
			var oStoreExtension = m_oExtensionProvider.GetExtension<IGooglePlayStoreExtensions>();
#endif // #if UNITY_IOS

			oStoreExtension.RestoreTransactions(this.OnRestoreProducts);
#else
			this.OnRestoreProducts(true);
#endif // #if UNITY_IOS || (UNITY_ANDROID && ANDROID_GOOGLE_PLATFORM)
		} else {
			CFunc.Invoke(ref a_oCallback, this, null, false);
		}
#else
		CFunc.Invoke(ref a_oCallback, this, null, false);
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 상품을 결제한다 */
	public void PurchaseProduct(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog($"CPurchaseManager.PurchaseProduct: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		var oProduct = this.GetProduct(a_oID);
		bool bIsEnablePurchase = !m_bIsPurchasing && (oProduct != null && oProduct.availableToPurchase);

		// 결제 가능 할 경우
		if(this.IsInit && bIsEnablePurchase) {
			m_bIsPurchasing = true;
			m_oCallbackDictA.ExReplaceVal(EPurchaseCallback.PURCHASE, a_oCallback);

			// 결제 된 상품 일 경우
			if(m_oPurchaseProductIDList.Contains(a_oID) || this.IsPurchaseNonConsumableProduct(oProduct)) {
				this.HandlePurchaseResult(a_oID, true);
			} else {
				m_oStoreController.InitiatePurchase(oProduct, KCDefine.U_PAYLOAD_PURCHASE_M_PURCHASE);
			}
		} else {
			CFunc.Invoke(ref a_oCallback, this, a_oID, false);
		}
#else
		CFunc.Invoke(ref a_oCallback, this, a_oID, false);
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}

	/** 결제를 확정한다 */
	public void ConfirmPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog($"CPurchaseManager.ConfirmPurchase: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_CONFIRM_PURCHASE_CALLBACK, () => {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
			var oProduct = this.GetProduct(a_oID);
			bool bIsEnableConfirm = m_bIsPurchasing && (oProduct != null && oProduct.availableToPurchase);

			// 결제 확정 가능 할 경우
			if(this.IsInit && bIsEnableConfirm) {
				m_oStoreController.ConfirmPendingPurchase(oProduct);
			}

			this.HandlePurchaseResult(a_oID, this.IsInit && bIsEnableConfirm, false, true);
			CFunc.Invoke(ref a_oCallback, this, a_oID, this.IsInit && bIsEnableConfirm);
#else
			CFunc.Invoke(ref a_oCallback, this, a_oID, false);
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		});
	}

	/** 결제를 거부한다 */
	public void RejectPurchase(string a_oID, System.Action<CPurchaseManager, string, bool> a_oCallback) {
		CFunc.ShowLog($"CPurchaseManager.RejectPurchase: {a_oID}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_REJECT_PURCHASE_CALLBACK, () => {
#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
			var oProduct = this.GetProduct(a_oID);
			bool bIsEnableReject = m_bIsPurchasing && (oProduct != null && oProduct.availableToPurchase);

			this.HandlePurchaseResult(a_oID, this.IsInit && bIsEnableReject, false, true);
			CFunc.Invoke(ref a_oCallback, this, a_oID, this.IsInit && bIsEnableReject);
#else
			CFunc.Invoke(ref a_oCallback, this, a_oID, false);
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		});
	}

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	/** 초기화되었을 경우 */
	private void OnInit(Task a_oTask) {
		CFunc.ShowLog($"CPurchaseManager.OnInit: {a_oTask.ExIsCompleteSuccess()}", KCDefine.B_LOG_COLOR_PLUGIN);

		// 초기화되었을 경우
		if(a_oTask.ExIsCompleteSuccess()) {
			var oProductDefinitionList = new List<ProductDefinition>();
			
			for(int i = 0; i < this.Params.m_oProductInfoList.Count; ++i) {
				CFunc.ShowLog($"CPurchaseManager.OnInit: {this.Params.m_oProductInfoList[i].m_oID}, {this.Params.m_oProductInfoList[i].m_eProductType}");

				CAccess.Assert(this.Params.m_oProductInfoList[i].m_oID.ExIsValid() && 
					this.Params.m_oProductInfoList[i].m_eProductType != ProductType.Subscription);

				var oProductDefinition = new ProductDefinition(this.Params.m_oProductInfoList[i].m_oID, 
					this.Params.m_oProductInfoList[i].m_eProductType);

				oProductDefinitionList.ExAddVal(oProductDefinition);
			}

			var oModule = StandardPurchasingModule.Instance();
			UnityPurchasing.Initialize(this, ConfigurationBuilder.Instance(oModule).AddProducts(oProductDefinitionList));
		}
	}

	/** 상품이 복원되었을 경우 */
	private void OnRestoreProducts(bool a_bIsSuccess) {
		CFunc.ShowLog($"CPurchaseManager.OnRestoreProducts: {a_bIsSuccess}", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_RESTORE_PRODUCTS_CALLBACK, () => {
			var oProductList = new List<Product>();

			// 복원되었을 경우
			if(a_bIsSuccess) {
				for(int i = 0; i < m_oStoreController.products.all.Length; ++i) {
					// 결제 된 비소모 상품 일 경우
					if(this.IsPurchaseNonConsumableProduct(m_oStoreController.products.all[i])) {
						oProductList.ExAddVal(m_oStoreController.products.all[i]);
					}

					this.RemovePurchaseProductID(m_oStoreController.products.all[i].definition.id);
				}
			}

			m_oCallbackDictB.GetValueOrDefault(EPurchaseCallback.RESTORE)?.Invoke(this, 
				oProductList, a_bIsSuccess && oProductList.ExIsValid());
		});
	}

	/** 결제 상품 식별자를 추가한다 */
	private void AddPurchaseProductID(string a_oID) {
		m_oPurchaseProductIDList.ExAddVal(a_oID);
		this.SavePurchaseProductIDs(m_oPurchaseProductIDList);
	}

	/** 결제 상품 식별자를 제거한다 */
	private void RemovePurchaseProductID(string a_oID) {
		m_oPurchaseProductIDList.ExRemoveVal(a_oID);
		this.SavePurchaseProductIDs(m_oPurchaseProductIDList);
	}

	/** 결제 상품 식별자를 로드한다 */
	private List<string> LoadPurchaseProductIDs() {
		return CFunc.ReadMsgPackObj<List<string>>(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS, true);
	}

	/** 결제 상품 식별자를 저장한다 */
	private void SavePurchaseProductIDs(List<string> a_oPurchaseProductIDList) {
		CFunc.WriteMsgPackObj<List<string>>(KCDefine.U_DATA_P_PURCHASE_PRODUCT_IDS, a_oPurchaseProductIDList, true);
	}

	/** 결제 결과를 처리한다 */
	private void HandlePurchaseResult(string a_oProductID, 
		bool a_bIsSuccess, bool a_bIsInvoke = true, bool a_bIsComplete = false) {

		CFunc.ShowLog($"CPurchaseManager.HandlePurchaseResult: {a_oProductID}, {a_bIsSuccess}, {a_bIsInvoke}, {a_bIsComplete}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_oProductID.ExIsValid());

		CScheduleManager.Inst.AddCallback(KCDefine.U_KEY_PURCHASE_M_HANDLE_PURCHASE_RESULT_CALLBACK, () => {
			// 완료되었을 경우
			if(a_bIsComplete) {
				m_bIsPurchasing = false;
				this.RemovePurchaseProductID(a_oProductID);
			}

			// 콜백 호출 모드 일 경우
			if(a_bIsInvoke) {
				m_oCallbackDictA.GetValueOrDefault(EPurchaseCallback.PURCHASE)?.Invoke(this, a_oProductID, a_bIsSuccess);
			}
		});
	}
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	#endregion // 함수

	#region 접근 함수
	/** 상품을 반환한다 */
	public Product GetProduct(string a_oID) {
		CAccess.Assert(a_oID.ExIsValid());

#if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
		return this.IsInit ? m_oStoreController.products.WithID(a_oID) : null;
#else
		return null;
#endif // #if UNITY_EDITOR || (UNITY_IOS || UNITY_ANDROID)
	}
	#endregion // 접근 함수

	#region 클래스 함수
#if UNITY_EDITOR
	/** 초기화 */
	[InitializeOnLoadMethod]
	public static void EditorInitialize() {
#if UNITY_ANDROID
#if ANDROID_AMAZON_PLATFORM
		UnityPurchasingEditor.TargetAndroidStore(AppStore.AmazonAppStore);
#else
		UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif // #if ANDROID_AMAZON_PLATFORM
#endif // #if UNITY_ANDROID
	}
#endif // #if UNITY_EDITOR
	#endregion // 클래스 함수

	#region 클래스 팩토리 함수
	/** 매개 변수를 생성한다 */
	public static STParams MakeParams(List<STProductInfo> a_oProductInfoList, 
		Dictionary<ECallback, System.Action<CPurchaseManager, bool>> a_oCallbackDict = null) {

		return new STParams() {
			m_oProductInfoList = a_oProductInfoList,
			m_oCallbackDict = a_oCallbackDict ?? new Dictionary<ECallback, System.Action<CPurchaseManager, bool>>()
		};
	}
	#endregion // 클래스 팩토리 함수
}
#endif // #if PURCHASE_MODULE_ENABLE
