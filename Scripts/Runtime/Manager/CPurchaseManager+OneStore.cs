using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if PURCHASE_MODULE_ENABLE
using UnityEngine.Purchasing;

#if ONE_STORE_PLATFORM
using Gaa;
#endif			// #if ONE_STORE_PLATFORM

//! 결제 관리자 - 원 스토어
public partial class CPurchaseManager : CSingleton<CPurchaseManager>, IStoreListener {
		
}
#endif			// #if PURCHASE_MODULE_ENABLE
