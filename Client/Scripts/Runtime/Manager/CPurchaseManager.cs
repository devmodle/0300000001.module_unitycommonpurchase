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
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener, IDetailedStoreListener
{
	#region IStoreListener
	/** 초기화되었을 경우 */
	public virtual void OnInitialized(IStoreController a_oControllerStore, IExtensionProvider a_oProviderExtension)
	{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		CFunc.ShowLog("CPurchaseManager.OnInitialized", KCDefine.B_LOG_COLOR_PLUGIN);

		CScheduleManager.Inst.AddCallback(KCDefine.G_PURCHASE_M_KEY_CALLBACK_INIT, () =>
		{
			m_oControllerStore = a_oControllerStore;
			m_oProviderExtension = a_oProviderExtension;

#if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)
			StandardPurchasingModule.Instance().useFakeStoreAlways = true;
			StandardPurchasingModule.Instance().useFakeStoreUIMode = FakeStoreUIMode.Default;
#endif // #if UNITY_EDITOR && (DEBUG || DEVELOPMENT_BUILD)

			this.IsInit = true;
			this.Params.m_oCallbackDict?.ExGetVal(ECallback.INIT)?.Invoke(this, this.IsInit);
		});
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason)
	{
		this.OnInitializeFailed(a_eReason, string.Empty);
	}

	/** 초기화에 실패했을 경우 */
	public virtual void OnInitializeFailed(InitializationFailureReason a_eReason, string? a_oMsg)
	{
#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		CFunc.ShowLogWarning($"CPurchaseManager.OnInitializeFailed: {a_eReason}, {a_oMsg}");

		CScheduleManager.Inst.AddCallback(KCDefine.G_PURCHASE_M_KEY_CALLBACK_FAIL_INIT, () =>
		{
			this.Params.m_oCallbackDict?.ExGetVal(ECallback.INIT)?.Invoke(this, false);
		});
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}
	#endregion // IStoreListener

	#region 함수
	/** 초기화 */
	public override void Awake()
	{
		base.Awake();

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		// 결제 상품 식별자 파일이 없을 경우
		if(!File.Exists(KCDefine.G_PURCHASE_M_DATA_P_IDS_PRODUCT_PURCHASE))
		{
			return;
		}

		var oListIDProductPurchase = this.LoadIDsProductPurchase();
		oListIDProductPurchase.ExCopyTo(m_oListIDProductPurchase, (a_oIDProduct) => a_oIDProduct);
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 초기화 */
	public virtual void Init(STParams a_stParams)
	{
		CFunc.ShowLog($"CPurchaseManager.Init: {a_stParams.m_oListInfoProduct}", KCDefine.B_LOG_COLOR_PLUGIN);
		CFunc.Assert(a_stParams.m_oListInfoProduct != null);

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		// 초기화되었을 경우
		if(this.IsInit)
		{
			goto PURCHASE_MANAGER_INIT_EXIT;
		}

		this.Params = a_stParams;

#if DEBUG || DEVELOPMENT_BUILD
		string oEnvironmentName = KCDefine.G_PURCHASE_M_ENVIRONMENT_N_DEV;
#else
		string oEnvironmentName = KCDefine.G_PURCHASE_M_ENVIRONMENT_N_PRODUCTION;
#endif // #if DEBUG || DEVELOPMENT_BUILD

		var oInitOpts = new InitializationOptions();
		var oAsyncTask = UnityServices.InitializeAsync(oInitOpts.SetEnvironmentName(oEnvironmentName));

		CManagerTask.Inst.WaitAsyncTask(oAsyncTask, this.OnInit);
		return;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID

PURCHASE_MANAGER_INIT_EXIT:
		a_stParams.m_oCallbackDict?.ExGetVal(ECallback.INIT)?.Invoke(this, this.IsInit);
	}

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	/** 초기화되었을 경우 */
	private void OnInit(Task a_oTask)
	{
		CFunc.ShowLog($"CPurchaseManager.OnInit: {a_oTask.ExIsCompleteSuccess()}", KCDefine.B_LOG_COLOR_PLUGIN);

		// 초기화에 실패했을 경우
		if(!a_oTask.ExIsCompleteSuccess())
		{
			goto PURCHASE_MANAGER_ON_INIT_EXIT;
		}

		var oListDefinitionProduct = new List<ProductDefinition>();

		for(int i = 0; i < this.Params.m_oListInfoProduct.Count; ++i)
		{
			CFunc.ShowLog($"CPurchaseManager.OnInit: {this.Params.m_oListInfoProduct[i].m_oID}, {this.Params.m_oListInfoProduct[i].m_eProductType}");

			CFunc.Assert(this.Params.m_oListInfoProduct[i].m_oID.ExIsValid() &&
				this.Params.m_oListInfoProduct[i].m_eProductType != ProductType.Subscription);

			var oProductDefinition = new ProductDefinition(this.Params.m_oListInfoProduct[i].m_oID,
				this.Params.m_oListInfoProduct[i].m_eProductType);

			oListDefinitionProduct.ExAddVal(oProductDefinition);
		}

		var oModule = StandardPurchasingModule.Instance();
		var oConfigurationBuilder = ConfigurationBuilder.Instance(oModule);

		UnityPurchasing.Initialize(this, oConfigurationBuilder.AddProducts(oListDefinitionProduct));
		return;

PURCHASE_MANAGER_ON_INIT_EXIT:
		this.OnInitializeFailed(InitializationFailureReason.AppNotKnown);
	}

	/** 결제 상품 식별자를 추가한다 */
	private void AddIDProductPurchase(string a_oID)
	{
		m_oListIDProductPurchase.ExAddVal(a_oID);
		this.SaveIDsProductPurchase(m_oListIDProductPurchase);
	}

	/** 결제 상품 식별자를 제거한다 */
	private void RemoveIDProductPurchase(string a_oID)
	{
		m_oListIDProductPurchase.ExRemoveVal(a_oID);
		this.SaveIDsProductPurchase(m_oListIDProductPurchase);
	}

	/** 결제 상품 식별자를 로드한다 */
	private List<string> LoadIDsProductPurchase()
	{
		return CFunc.ReadMsgPackObj<List<string>>(KCDefine.G_PURCHASE_M_DATA_P_IDS_PRODUCT_PURCHASE, true);
	}

	/** 결제 상품 식별자를 저장한다 */
	private void SaveIDsProductPurchase(List<string> a_oListIDProductPurchase)
	{
		CFunc.WriteMsgPackObj<List<string>>(KCDefine.G_PURCHASE_M_DATA_P_IDS_PRODUCT_PURCHASE, a_oListIDProductPurchase, true);
	}
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	#endregion // 함수

	#region 접근 함수
	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseProductConsumableNon(string a_oIDProduct)
	{
		CFunc.Assert(a_oIDProduct.ExIsValid());

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		var oProduct = this.GetProduct(a_oIDProduct);
		return this.IsInit ? this.IsPurchaseProductConsumableNon(oProduct) : false;
#else
		return false;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 비소모 상품 결제 여부를 검사한다 */
	public bool IsPurchaseProductConsumableNon(Product a_oProduct)
	{
		CFunc.Assert(a_oProduct != null);

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		return this.IsInit && a_oProduct.hasReceipt && a_oProduct.definition.type == ProductType.NonConsumable;
#else
		return false;
#endif // #if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
	}

	/** 상품을 반환한다 */
	public Product GetProduct(string a_oIDProduct)
	{
		CFunc.Assert(a_oIDProduct.ExIsValid());

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID
		return this.IsInit ? m_oControllerStore.products.WithID(a_oIDProduct) : null;
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
		Dictionary<ECallback, System.Action<CPurchaseManager, bool>> a_oCallbackDict = null)
	{
		return new STParams()
		{
			m_oListInfoProduct = a_oProductInfoList,
			m_oCallbackDict = a_oCallbackDict ?? new Dictionary<ECallback, System.Action<CPurchaseManager, bool>>()
		};
	}
	#endregion // 클래스 팩토리 함수
}
#endif // #if PURCHASE_MODULE_ENABLE
