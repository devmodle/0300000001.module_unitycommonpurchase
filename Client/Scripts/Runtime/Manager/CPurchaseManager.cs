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

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	private IStoreController m_oStoreController = null;
	private IExtensionProvider m_oExtensionProvider = null;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 변수

	#region 프로퍼티
	public STParams Params { get; private set; }
	public bool IsInit { get; private set; } = false;
	#endregion // 프로퍼티

	#region IStoreListener
	/** 초기화되었을 경우 */
	public virtual void OnInitialized(IStoreController a_oStoreController, IExtensionProvider a_oExtensionProvider) {
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_INIT_CALLBACK, () => {
			m_oStoreController = a_oStoreController;
			m_oExtensionProvider = a_oExtensionProvider;

#if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)
			StandardPurchasingModule.Instance().useFakeStoreAlways = true;
			StandardPurchasingModule.Instance().useFakeStoreUIMode = FakeStoreUIMode.Default;
#endif // #if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)

			this.IsInit = true;
			this.Params.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, this.IsInit);
		});
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason) {
		this.OnInitializeFailed(a_eReason, string.Empty);
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason, string? a_oMsg) {
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		CFunc.ShowLogWarning($"CPurchaseManager.OnInitializeFailed: {a_eReason}, {a_oMsg}");

		CScheduleManager.Inst.AddCallback(KCDefine.B_KEY_PURCHASE_M_INIT_FAIL_CALLBACK, () => {
			this.Params.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, false);
		});
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}
	#endregion // IStoreListener

	#region 함수
	/** 초기화 */
	public override void Awake() {
		base.Awake();

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		// 결제 상품 식별자 파일이 없을 경우
		if(!File.Exists(KCDefine.B_DATA_P_PURCHASE_PRODUCT_IDS)) {
			return;
		}

		var oPurchaseProductIDList = this.LoadPurchaseProductIDs();
		oPurchaseProductIDList.ExCopyTo(m_oPurchaseProductIDList, (a_oProductID) => a_oProductID);
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 초기화 */
	public virtual void Init(STParams a_stParams) {
		CFunc.ShowLog($"CPurchaseManager.Init: {a_stParams.m_oProductInfoList}", KCDefine.B_LOG_COLOR_PLUGIN);
		CAccess.Assert(a_stParams.m_oProductInfoList != null);

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		// 초기화되었을 경우
		if(this.IsInit) {
			goto PURCHASE_MANAGER_INIT_EXIT;
		}

		this.Params = a_stParams;

#if DEBUG || DEVELOPMENT_BUILD
		string oEnvironmentName = KCDefine.B_ENVIRONMENT_N_DEV;
#else
		string oEnvironmentName = KCDefine.B_ENVIRONMENT_N_PRODUCTION;
#endif // #if DEBUG || DEVELOPMENT_BUILD

		var oInitOpts = new InitializationOptions();
		var oAsyncTask = UnityServices.InitializeAsync(oInitOpts.SetEnvironmentName(oEnvironmentName));

		CTaskManager.Inst.WaitAsyncTask(oAsyncTask, this.OnInit);
		return;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID

PURCHASE_MANAGER_INIT_EXIT:
		a_stParams.m_oCallbackDict?.GetValueOrDefault(ECallback.INIT)?.Invoke(this, this.IsInit);
	}

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	/** 초기화되었을 경우 */
	private void OnInit(Task a_oTask) {
		CFunc.ShowLog($"CPurchaseManager.OnInit: {a_oTask.ExIsCompleteSuccess()}", KCDefine.B_LOG_COLOR_PLUGIN);

		// 초기화에 실패했을 경우
		if(!a_oTask.ExIsCompleteSuccess()) {
			goto PURCHASE_MANAGER_ON_INIT_EXIT;
		}

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
		var oConfigurationBuilder = ConfigurationBuilder.Instance(oModule);

		UnityPurchasing.Initialize(this, oConfigurationBuilder.AddProducts(oProductDefinitionList));
		return;

PURCHASE_MANAGER_ON_INIT_EXIT:
		this.OnInitializeFailed(InitializationFailureReason.AppNotKnown);
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
		return CFunc.ReadMsgPackObj<List<string>>(KCDefine.B_DATA_P_PURCHASE_PRODUCT_IDS, true);
	}

	/** 결제 상품 식별자를 저장한다 */
	private void SavePurchaseProductIDs(List<string> a_oPurchaseProductIDList) {
		CFunc.WriteMsgPackObj<List<string>>(KCDefine.B_DATA_P_PURCHASE_PRODUCT_IDS, a_oPurchaseProductIDList, true);
	}
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 함수

	#region 접근 함수
	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseNonConsumableProduct(string a_oProductID) {
		CAccess.Assert(a_oProductID.ExIsValid());

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		var oProduct = this.GetProduct(a_oProductID);
		return this.IsInit ? this.IsPurchaseNonConsumableProduct(oProduct) : false;
#else
		return false;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseNonConsumableProduct(Product a_oProduct) {
		CAccess.Assert(a_oProduct != null);

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		return this.IsInit && a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable;
#else
		return false;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 상품을 반환한다 */
	public Product GetProduct(string a_oProductID) {
		CAccess.Assert(a_oProductID.ExIsValid());

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		return this.IsInit ? m_oStoreController.products.WithID(a_oProductID) : null;
#else
		return null;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}
	#endregion // 접근 함수

	#region 클래스 함수
#if UNITY_EDITOR && UNITY_ANDROID
	/** 초기화 */
	[InitializeOnLoadMethod]
	public static void EditorInitialize() {
#if ANDROID_AMAZON_PLATFORM
		UnityPurchasingEditor.TargetAndroidStore(AppStore.AmazonAppStore);
#else
		UnityPurchasingEditor.TargetAndroidStore(AppStore.GooglePlay);
#endif // #if ANDROID_AMAZON_PLATFORM
	}
#endif // #if UNITY_EDITOR && UNITY_ANDROID
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
