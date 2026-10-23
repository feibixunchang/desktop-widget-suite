namespace WeatherWidget48;

public static class LocationCatalog
{
    private static readonly IReadOnlyList<GeoLocation> Locations = new[]
    {
        new GeoLocation("北京", "北京市", "北京市", "中国", 39.9075, 116.39723),
        new GeoLocation("上海", "上海市", "上海市", "中国", 31.22222, 121.45806),
        new GeoLocation("广州", "广东", "广州市", "中国", 23.12911, 113.26438),
        new GeoLocation("深圳", "广东", "深圳市", "中国", 22.54554, 114.06830),
        new GeoLocation("成都", "四川", "成都市", "中国", 30.66667, 104.06667),
        new GeoLocation("杭州", "浙江", "杭州市", "中国", 30.27415, 120.15515),
        new GeoLocation("南京", "江苏", "南京市", "中国", 32.06025, 118.79688),
        new GeoLocation("武汉", "湖北", "武汉市", "中国", 30.59285, 114.30549),
        new GeoLocation("西安", "陕西", "西安市", "中国", 34.34157, 108.94017),
        new GeoLocation("重庆", "重庆", "重庆市", "中国", 29.56301, 106.55156),
        new GeoLocation("苏州", "江苏", "苏州市", "中国", 31.29889, 120.58533),
        new GeoLocation("天津", "天津", "天津市", "中国", 39.34344, 117.36158),
        new GeoLocation("青岛", "山东", "青岛市", "中国", 36.06708, 120.38264),
        new GeoLocation("长沙", "湖南", "长沙市", "中国", 28.22821, 112.93881),
        new GeoLocation("郑州", "河南", "郑州市", "中国", 34.74660, 113.62536),
        new GeoLocation("济南", "山东", "济南市", "中国", 36.65117, 117.12009),
        new GeoLocation("福州", "福建", "福州市", "中国", 26.07451, 119.29649),
        new GeoLocation("厦门", "福建", "厦门市", "中国", 24.47983, 118.08942),
        new GeoLocation("合肥", "安徽", "合肥市", "中国", 31.82069, 117.22722),
        new GeoLocation("宁波", "浙江", "宁波市", "中国", 29.86829, 121.54400),
        new GeoLocation("东莞", "广东", "东莞市", "中国", 23.02068, 113.75180),
        new GeoLocation("佛山", "广东", "佛山市", "中国", 23.02198, 113.12141),
        new GeoLocation("沈阳", "辽宁", "沈阳市", "中国", 41.80570, 123.43147),
        new GeoLocation("大连", "辽宁", "大连市", "中国", 38.91400, 121.61470),
        new GeoLocation("哈尔滨", "黑龙江", "哈尔滨市", "中国", 45.80377, 126.53497),
        new GeoLocation("长春", "吉林", "长春市", "中国", 43.81707, 125.32354),
        new GeoLocation("石家庄", "河北", "石家庄市", "中国", 38.04276, 114.51495),
        new GeoLocation("太原", "山西", "太原市", "中国", 37.87059, 112.54888),
        new GeoLocation("南宁", "广西", "南宁市", "中国", 22.81547, 108.36654),
        new GeoLocation("贵阳", "贵州", "贵阳市", "中国", 26.64702, 106.63020),
        new GeoLocation("海口", "海南", "海口市", "中国", 20.04400, 110.19989),
        new GeoLocation("拉萨", "西藏", "拉萨市", "中国", 29.64555, 91.14086),
        new GeoLocation("乌鲁木齐", "新疆", "乌鲁木齐市", "中国", 43.82559, 87.61685),
        new GeoLocation("兰州", "甘肃", "兰州市", "中国", 36.06109, 103.83430),
        new GeoLocation("西宁", "青海", "西宁市", "中国", 36.61714, 101.77816),
        new GeoLocation("银川", "宁夏", "银川市", "中国", 38.48719, 106.23091)
    };

    public static IReadOnlyList<GeoLocation> Search(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<GeoLocation>();
        }

        return Locations
            .Where(location => location.Name.ContainsOrdinal(normalized)
                || location.Admin1.ContainsOrdinal(normalized)
                || location.Admin2.ContainsOrdinal(normalized))
            .OrderByDescending(location => string.Equals(location.Name, normalized, StringComparison.Ordinal))
            .ThenBy(location => location.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
