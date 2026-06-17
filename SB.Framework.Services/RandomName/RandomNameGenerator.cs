namespace SB.Framework.Services
{
    /// <summary>
    /// <see cref="IRandomNameGenerator"/> 默认实现。纯内存字库 + 组合模板,线程安全(Random.Shared)。
    /// 接口绑定在框架管线 AddSbFramework 里显式注册(业务层零配置注入)。
    /// </summary>
    public sealed class RandomNameGenerator : IRandomNameGenerator
    {
        // ── 单姓(常见百家姓)──
        private const string Surnames =
            "王李张刘陈杨黄赵吴周徐孙马朱胡郭何高林罗郑梁谢宋唐许韩冯邓曹彭曾肖田董袁潘于蒋蔡余杜叶程苏魏吕丁任沈姚卢" +
            "姜崔钟谭陆汪范金石廖贾夏韦付方白邹孟熊秦邱江尹薛闫段雷侯龙史陶黎贺顾毛郝龚邵万钱严覃武戴莫孔向汤";

        // ── 复姓(少量,低概率出现)──
        private static readonly string[] CompoundSurnames =
            { "欧阳", "司马", "上官", "诸葛", "东方", "皇甫", "尉迟", "公孙", "慕容", "夏侯", "令狐", "宇文" };

        // ── 名用字(混合,不强分性别)──
        private const string GivenChars =
            "伟刚勇毅俊峰强军平保东文辉力明永健世广志义兴良海山仁波宁贵福生龙元全国胜学祥才发武新利清飞彬富顺信子杰涛昌成康星" +
            "光天达安岩中茂进林有坚和彪博诚先敬震振壮会思群豪心邦承乐绍功松善厚庆磊民友裕河哲江超浩亮政谦亨奇固之轮翰朗伯宏言" +
            "若鸣朋斌梁栋维启克伦翔旭鹏泽晨辰士建家致树炎德行时泰盛雄琛钧冠策腾楠榕风航弘" +
            "秀娟英华慧巧美娜静淑惠珠雅芝玉萍红娥玲芬芳燕彩春菊兰凤洁梅琳素云莲真雪荣爱妹霞香月莺艳瑞凡佳嘉琼勤珍贞莉桂娴瑾颖" +
            "婷姣婉瑶怡婵雁蓓仪荷丹蓉君琴蕊薇菁梦岚苑婕馨韵融艺咏卿澜纯悦冰爽琬茗羽欣飘滢馥柔竹凝晓欢霄枫芸菲寒伊宜可舒影思丽";

        // ── 服务名组合素材 ──
        private static readonly string[] SendThings =
            { "快递", "水", "餐", "煤气", "菜", "花", "药", "牛奶", "外卖", "桶装水", "纯净水", "气", "报纸", "鲜花" };
        private static readonly string[] GuyJobs =
            { "快递", "外卖", "跑腿", "送水", "搬家", "闪送", "代驾" };
        private static readonly string[] Places =
            { "楼下", "小区", "街角", "门口", "对面", "隔壁", "巷口", "村口", "路口", "拐角" };
        private static readonly string[] Shops =
            { "饭店", "便利店", "超市", "理发店", "水果店", "药店", "菜市场", "小卖部", "早餐店", "粉店", "面馆", "烧烤店", "奶茶店", "餐馆", "快餐店" };
        private static readonly string[] FixThings =
            { "空调", "手机", "电脑", "水管", "鞋", "锁", "家电", "自行车", "电动车", "灯" };
        private static readonly string[] Brands =
            { "京东", "顺丰", "中通", "圆通", "韵达", "申通", "菜鸟", "美团", "饿了么", "邮政", "极兔", "德邦" };
        private static readonly string[] BrandServices = { "快递", "驿站", "外卖" };
        private static readonly string[] FixedServices =
            { "物业", "房东", "中介", "装修队", "水电工", "收废品的", "代驾", "滴滴司机", "搬家公司", "通下水道的",
              "开锁的", "家政", "保洁阿姨", "月嫂", "快递驿站", "物业管家", "楼管", "保安", "居委会", "送水的", "订餐", "跑腿小哥" };

        public string ChineseName()
        {
            var r = Rnd;
            // 5% 概率复姓
            string surname = r.Next(100) < 5
                ? CompoundSurnames[r.Next(CompoundSurnames.Length)]
                : Surnames[r.Next(Surnames.Length)].ToString();
            // 名 1~2 字(2 字占多数)
            int len = r.Next(100) < 75 ? 2 : 1;
            var given = len == 1
                ? GivenChars[r.Next(GivenChars.Length)].ToString()
                : new string(new[] { GivenChars[r.Next(GivenChars.Length)], GivenChars[r.Next(GivenChars.Length)] });
            return surname + given;
        }

        public string ServiceName()
        {
            var r = Rnd;
            return r.Next(7) switch
            {
                0 => $"送{Pick(SendThings)}的",
                1 => $"{Pick(GuyJobs)}小哥",
                2 => $"{Pick(Places)}{Pick(Shops)}",
                3 => $"修{Pick(FixThings)}的",
                4 => $"{Pick(Brands)}{Pick(BrandServices)}",
                _ => Pick(FixedServices), // 固定池权重更高
            };
        }

        public IReadOnlyList<string> ChineseNames(int count) => Distinct(count, ChineseName);
        public IReadOnlyList<string> ServiceNames(int count) => Distinct(count, ServiceName);

        // ── helpers ──
        private static Random Rnd => Random.Shared;
        private static string Pick(string[] pool) => pool[Rnd.Next(pool.Length)];

        /// <summary>生成 count 个,尽力去重(服务名空间有限,去重失败时允许重复以保证数量)。</summary>
        private static List<string> Distinct(int count, Func<string> gen)
        {
            if (count <= 0) return new List<string>();
            var set = new HashSet<string>();
            var list = new List<string>(count);
            int guard = 0, maxTries = count * 20 + 50;
            while (list.Count < count && guard++ < maxTries)
            {
                var v = gen();
                if (set.Add(v)) list.Add(v);
            }
            // 兜底:去重凑不够数量时直接补(允许重复)
            while (list.Count < count) list.Add(gen());
            return list;
        }
    }
}
