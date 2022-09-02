using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Gaa;

/// <summary>
/// OneStore 内购接入：
/// https://dev.onestore.co.kr/devpoc/reference/view/Apps
/// </summary>
public class MainUIScript : MonoBehaviour
{
    private static readonly string TAG = "MainUIScript";

    // Product ID registered in the Developer Center
    private string[] all_products =
    {
        "pay_50dia", 
        "pay_260dia", 
        "pay_480dia", 
        "pay_980dia", 
        "pay_1980dia", 
        "pay_3280dia",
        "pay_4680dia",
        "pay_6480dia", 
        "pay_month"
    };

    [TextAreaAttribute] 
    public string base64EncodedPublicKey = "";

    private LogScript logger;

    public GameObject productDetailItem;
    public ScrollRect productDetailListView;
    public RectTransform detailContent;

    public GameObject purchaseItem;
    public ScrollRect purchaseListView;
    public RectTransform purchaseContent;

    public GameObject scrollLog;
    public GameObject loading;

    private List<ProductDetail> productDetails;

    private Dictionary<string, PurchaseData> purchaseMap = new Dictionary<string, PurchaseData>();
    private Dictionary<string, string> signatureMap = new Dictionary<string, string>();

    Text pointView;
    Button connection;

    enum PurchaseButtonState
    {
        NONE,
        ACKNOWLEDGE,
        CONSUME,
        REACTIVE,
        CANCEL
    };

    void Awake()
    {
        GaaIapResultListener.OnLoadingVisibility += OnLoadingVisibility;

        GaaIapResultListener.PurchaseClientStateResponse += PurchaseClientStateResponse;
        GaaIapResultListener.OnPurchaseUpdatedResponse += OnPurchaseUpdatedResponse;
        GaaIapResultListener.OnQueryPurchasesResponse += OnQueryPurchasesResponse;
        GaaIapResultListener.OnProductDetailsResponse += OnProductDetailsResponse;

        GaaIapResultListener.OnConsumeSuccessResponse += OnConsumeSuccessResponse;
        GaaIapResultListener.OnAcknowledgeSuccessResponse += OnAcknowledgeSuccessResponse;
        GaaIapResultListener.OnManageRecurringResponse += OnManageRecurringResponse;

        GaaIapResultListener.SendLog += SendLog;
    }

    void OnDestroy()
    {
        GaaIapResultListener.OnLoadingVisibility -= OnLoadingVisibility;

        GaaIapResultListener.PurchaseClientStateResponse -= PurchaseClientStateResponse;
        GaaIapResultListener.OnPurchaseUpdatedResponse -= OnPurchaseUpdatedResponse;
        GaaIapResultListener.OnQueryPurchasesResponse -= OnQueryPurchasesResponse;
        GaaIapResultListener.OnProductDetailsResponse -= OnProductDetailsResponse;

        GaaIapResultListener.OnConsumeSuccessResponse -= OnConsumeSuccessResponse;
        GaaIapResultListener.OnAcknowledgeSuccessResponse -= OnAcknowledgeSuccessResponse;
        GaaIapResultListener.OnManageRecurringResponse -= OnManageRecurringResponse;

        GaaIapResultListener.SendLog -= SendLog;

        GaaIapCallManager.Destroy();
    }

    void Start()
    {
        logger = GameObject.Find("LogScript").GetComponent<LogScript>();
        pointView = GameObject.Find("Point").GetComponent<Text>();
        UpdatePoint(PlayerPrefs.GetInt("Point"));

        connection = GameObject.Find("Connection").GetComponent<Button>();

        StartCoroutine(StartConnectService());

        AddItems();
    }


    void AddItems()
    {
        foreach (var productId in all_products)
        {
            SendLog(TAG, "ProductDetail: " + productId);
            GameObject scrollItem = Instantiate<GameObject>(productDetailItem, transform);
            scrollItem.transform.SetParent(detailContent.transform, false);
            scrollItem.transform.Find("Text").gameObject.GetComponent<Text>().text = productId;


            string id = productId;
            string type = ProductType.INAPP;
            scrollItem.GetComponent<Button>().onClick.AddListener(() => OnDetailItemClick(id, type));
        }
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }
    }

    PurchaseData GetPurchaseData(string productId)
    {
        PurchaseData pData = null;
        foreach (KeyValuePair<string, PurchaseData> pair in purchaseMap)
        {
            if (productId.Equals(pair.Key))
            {
                pData = pair.Value;
                break;
            }
        }

        return pData;
    }

    public void SendLog(string tag, string message)
    {
        if (logger != null)
            logger.Log(tag, message);
        else
            Debug.Log("[" + tag + "]: " + message);
    }

    // ======================================================================================
    // Mange Point
    // ======================================================================================

    public void UsePoint(int used)
    {
        int point = PlayerPrefs.GetInt("Point");
        if (point >= used)
        {
            int result = point - used;
            PlayerPrefs.SetInt("Point", result);
            UpdatePoint(result);
        }
        else
        {
            SendLog(TAG, "UsePoint: There are not enough points to use.");
        }
    }

    public void AddPoint(int point)
    {
        SendLog(TAG, "AddPorint: " + point + " point");
        int savedPoint = PlayerPrefs.GetInt("Point");
        int result = savedPoint + point;
        PlayerPrefs.SetInt("Point", result);
        UpdatePoint(result);
    }

    void UpdatePoint(int point)
    {
        pointView.text = "Point: <color=#ff0000>" + point + "</color>";
    }


    // ======================================================================================
    // Request
    // ======================================================================================

    IEnumerator StartConnectService()
    {
        yield return new WaitForSeconds(1.0f);
        StartConnection();
    }

    public void StartConnection()
    {
        SendLog(TAG, "StartConnection()");
        if (GaaIapCallManager.IsServiceAvailable() == false)
        {
            OnLoadingVisibility(true);
            GaaIapCallManager.StartConnection(base64EncodedPublicKey);
        }
        else
        {
            SendLog(TAG, "StartConnection: Already connected to the payment module.");
        }
    }

    void BuyProduct(string productId, string type)
    {
        SendLog(TAG, "BuyProduct - productId: " + productId + ", type: " + type);

        PurchaseFlowParams param = new PurchaseFlowParams();
        param.productId = productId;
        param.productType = type;
        //param.productName = "";
        //param.devPayload = "your Developer Payload";
        //param.gameUserId = "";
        //param.promotionApplicable = false;

        GaaIapCallManager.LaunchPurchaseFlow(param);
    }

    void QueryPurchases()
    {
        OnLoadingVisibility(true);
        purchaseMap.Clear();
        signatureMap.Clear();

        // Delete all items in the purchase history list UI.
        foreach (Transform child in purchaseContent)
            Destroy(child.gameObject);

        GaaIapCallManager.QueryPurchases(ProductType.INAPP);
        GaaIapCallManager.QueryPurchases(ProductType.AUTO);
    }

    //对于消耗性产品，请使用GaaIapCallManager.Consume()进行确认购买
    void ConsumePurchase(string productId)
    {
        SendLog(TAG, "ConsumePurchase: productId: " + productId);
        PurchaseData purchaseData = GetPurchaseData(productId);
        if (purchaseData != null)
        {
            OnLoadingVisibility(true);
            GaaIapCallManager.Consume(purchaseData, /*developerPayload*/null);
        }
        else
        {
            SendLog(TAG, "ConsumePurchase: purchase data is null.");
        }
    }

    //非消耗性产品，请使用 GaaIapCallManager.Acknowledge()进行确认购买
    void AcknowledgePurchase(string productId)
    {
        SendLog(TAG, "AcknowledgePurchase: productId: " + productId);
        PurchaseData purchaseData = GetPurchaseData(productId);
        if (purchaseData != null)
        {
            OnLoadingVisibility(true);
            GaaIapCallManager.Acknowledge(purchaseData, /*developerPayload*/null);
        }
        else
        {
            SendLog(TAG, "AcknowledgePurchase: purchase data is null.");
        }
    }

    void ManageRecurringProduct(string productId)
    {
        SendLog(TAG, "ManageRecurringProduct: productId: " + productId);
        PurchaseData purchaseData = GetPurchaseData(productId);
        if (purchaseData != null)
        {
            OnLoadingVisibility(true);
            string recurringAction = RecurringAction.REACTIVATE;
            if (purchaseData.recurringState == RecurringState.RECURRING)
            {
                recurringAction = RecurringAction.CANCEL;
            }

            GaaIapCallManager.ManageRecurringProduct(purchaseData, recurringAction);
        }
        else
        {
            SendLog(TAG, "ManageRecurringProduct: purchase data is null.");
        }
    }


    // ======================================================================================
    // Response
    // ======================================================================================

    void OnLoadingVisibility(bool visibility)
    {
        loading.SetActive(visibility);
    }

    void PurchaseClientStateResponse(IapResult iapResult)
    {
        SendLog(TAG, "PurchaseClientStateResponse:\n\t\t-> " + iapResult.ToString());
        Text text = connection.transform.Find("Text").GetComponent<Text>();
        if (iapResult.IsSuccess())
        {
            text.text = "Connected";
            QueryPurchases();
            GaaIapCallManager.QueryProductDetails(all_products, ProductType.ALL);
        }
        else
        {
            text.text = "Disconnected";
            GaaIapResultListener.HandleError("PurchaseClientStateResponse", iapResult);
        }
    }

    /// <summary>
    /// 购买成功
    /// </summary>
    void OnPurchaseUpdatedResponse(List<PurchaseData> purchases, List<string> signatures)
    {
        ParsePurchaseData("OnPurchaseUpdatedResponse", purchases, signatures);
    }

    /// <summary>
    /// 查询成功
    /// </summary>
    void OnQueryPurchasesResponse(List<PurchaseData> purchases, List<string> signatures)
    {
        ParsePurchaseData("OnQueryPurchasesResponse", purchases, signatures);
    }

    private void ParsePurchaseData(string func, List<PurchaseData> purchases, List<string> signatures)
    {
        SendLog(TAG, func);
        for (int i = 0; i < purchases.Count; i++)
        {
            PurchaseData p = purchases[i];
            string s = signatures[i];

            purchaseMap.Add(p.productId, p);
            signatureMap.Add(p.productId, s);

            GameObject scrollItem = Instantiate<GameObject>(purchaseItem, transform);
            scrollItem.SetActive(true);
            scrollItem.transform.SetParent(purchaseContent.transform, false);
            Text title = scrollItem.transform.Find("Title").gameObject.GetComponent<Text>();
            title.text = p.productId;

            PurchaseButtonState state = PurchaseButtonState.CONSUME;
//            if (p.productId.Equals(inapp_p50000))
//            {
//                if (p.IsAcknowledged() == true)
//                    state = PurchaseButtonState.CONSUME;
//                else
//                    state = PurchaseButtonState.ACKNOWLEDGE;
//            }
//            else if (p.productId.Equals(auto_a100000))
//            {
//                if (p.IsAcknowledged() == true)
//                {
//                    if (p.recurringState == RecurringState.RECURRING)
//                    {
//                        state = PurchaseButtonState.CANCEL;
//                        title.text += "\n(subscribing)";
//                    }
//                    else if (p.recurringState == RecurringState.CANCEL)
//                    {
//                        state = PurchaseButtonState.REACTIVE;
//                        title.text += "\n(cancel subscription)";
//                    }
//                }
//                else
//                    state = PurchaseButtonState.ACKNOWLEDGE;
//            }
//            else
//            {
//                state = PurchaseButtonState.CONSUME;
//            }

            string buttonText = state.ToString();
            GameObject buttonObj = scrollItem.transform.Find("Button").gameObject;
            GameObject btnText = buttonObj.transform.Find("Text").gameObject;
            btnText.GetComponent<Text>().text = buttonText;

            string id = p.productId;
            buttonObj.GetComponent<Button>().onClick.AddListener(() => OnPurchaseItemClick(id, state));

            SendLog(TAG, "PurchaseData[" + i + "]: " + p.productId);
        }
    }

    void OnPurchaseItemClick(string productId, PurchaseButtonState state)
    {
        SendLog(TAG, "OnPurchaseItemClick:\n\t\t-> productId: " + productId + ", state: " + state.ToString());
        switch (state)
        {
            case PurchaseButtonState.ACKNOWLEDGE:
                AcknowledgePurchase(productId);
                break;
            case PurchaseButtonState.CONSUME:
                ConsumePurchase(productId);
                break;
            case PurchaseButtonState.REACTIVE:
            case PurchaseButtonState.CANCEL:
                ManageRecurringProduct(productId);
                break;
        }
    }

    void OnProductDetailsResponse(List<ProductDetail> products)
    {
        SendLog(TAG, "OnProductDetailsResponse()");
        productDetails = products;

        foreach (ProductDetail detail in productDetails)
        {
            SendLog(TAG, "ProductDetail: " + detail.title);
            GameObject scrollItem = Instantiate<GameObject>(productDetailItem, transform);
            scrollItem.SetActive(true);
            scrollItem.transform.SetParent(detailContent.transform, false);
            scrollItem.transform.Find("Text").gameObject.GetComponent<Text>().text = detail.title;


            string id = detail.productId;
            string type = detail.type;
            scrollItem.GetComponent<Button>().onClick.AddListener(() => OnDetailItemClick(id, type));
        }
    }

    void OnDetailItemClick(string productId, string productType)
    {
        BuyProduct(productId, productType);
    }

    void OnConsumeSuccessResponse(PurchaseData purchaseData)
    {
//        if (purchaseData.productId.Equals(inapp_p5000))
//            AddPoint(500);
//        else if (purchaseData.productId.Equals(inapp_p10000))
//            AddPoint(1000);

        SendLog(TAG, "OnConsumeSuccessResponse:\n\t\t-> productId: " + purchaseData.productId);
        purchaseMap.Remove(purchaseData.productId);
        signatureMap.Remove(purchaseData.productId);

        foreach (Transform child in purchaseContent)
        {
            string text = child.Find("Title").gameObject.GetComponent<Text>().text;
            if (purchaseData.productId.Equals(text))
            {
                Destroy(child.gameObject);
                break;
            }
        }
    }

    void OnAcknowledgeSuccessResponse(PurchaseData purchaseData)
    {
        SendLog(TAG, "OnAcknowledgeSuccessResponse:\n\t\t-> productId: " + purchaseData.productId);
        QueryPurchases();
    }

    void OnManageRecurringResponse(PurchaseData purchaseData, string action)
    {
        SendLog(TAG, "OnManageRecurringResponse:\n\t\t-> productId: " + purchaseData.productId + ", action: " + action);
        QueryPurchases();
    }
}