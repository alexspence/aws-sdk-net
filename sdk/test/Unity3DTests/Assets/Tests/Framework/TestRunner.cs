﻿using Amazon;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ThirdParty.Json.LitJson;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using System.IO;
using ThirdParty.iOS4Unity;

namespace AWSSDK.Tests.Framework
{
    public class TestRunner : ITestListener
    {
        public static AWSCredentials Credentials { get; private set; }

        public static RegionEndpoint RegionEndpoint { get; private set; }

        public static string TestAccount { get; private set; }

        private static bool Loaded = false;
        private TextWriter LogWriter { get; set; }

        public TestRunner()
        {
            if (!Loaded)
            {
                var resource = Resources.Load(@"settings") as TextAsset;
                var settings = JsonMapper.ToObject(resource.text);
                Credentials = new BasicAWSCredentials(settings["AccessKeyId"].ToString(), settings["SecretAccessKey"].ToString());
                RegionEndpoint = RegionEndpoint.GetBySystemName(settings["RegionEndpoint"].ToString());
                Loaded = true;
                LogWriter = new StringWriter();
            }
        }

        public void RunTests()
        {
            MissingAPILambdaFunctions.Initialize();
            ITestAssemblyRunner runner = null;
            if (IsIL2CPP)
                runner = new NUnitTestAssemblyRunner(new UnityTestAssemblyBuilder());
            else
                runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
            var currentAssembly = this.GetType().Assembly;
            var options = new Dictionary<string, string>();
            var tests = runner.Load(currentAssembly, options);
            var result = runner.Run(this, new FixtureAndCaseFilter("PutObjectTests"));
        }

        /// <summary>
        /// Determines if Unity scripting backend is IL2CPP.
        /// </summary>
        /// <returns><c>true</c>If scripting backend is IL2CPP; otherwise, <c>false</c>.</returns>
        internal static bool IsIL2CPP
        {
            get
            {
                Type type = Type.GetType("Mono.Runtime");
                if (type != null)
                {
                    MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                    if (displayName != null)
                    {
                        string name = null;
                        try
                        {
                            name = displayName.Invoke(null, null).ToString();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (name != null && name.ToUpper().Contains("IL2CPP"))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        private class CategoryFilter : ITestFilter
        {
            private string CategoryName;
            private const string CategoryKey = "Category";

            public CategoryFilter(string categoryName)
            {
                this.CategoryName = categoryName;
            }

            bool ITestFilter.IsExplicitMatch(ITest test)
            {
                return false;
            }

            bool ITestFilter.Pass(ITest test)
            {
                if (test.Method == null)
                    return true;

                if (string.IsNullOrEmpty(CategoryName))
                    return true;

                var testName = test.Name;
                var categories = (test.Properties.ContainsKey(CategoryKey)) ?
                    test.Properties[CategoryKey] : new List<string>();

                var stringCategories = categories.Cast<string>().ToList();

                return stringCategories.Contains(CategoryName);
            }

            public TNode ToXml(bool b)
            {
                return null;
            }

            public TNode AddToXml(TNode n, bool b)
            {
                return null;
            }
        }

        private class FixtureAndCaseFilter : ITestFilter
        {
            private HashSet<String> FixtureNames;
            private HashSet<String> TestCaseNames;

            public FixtureAndCaseFilter(HashSet<String> fixtureNames, HashSet<String> testCaseNames)
            {
                FixtureNames = fixtureNames;
                TestCaseNames = testCaseNames;
            }

            public FixtureAndCaseFilter(HashSet<String> fixtureNames)
            {
                FixtureNames = fixtureNames;
                TestCaseNames = new HashSet<string>();
            }

            public FixtureAndCaseFilter(string fixtureName, string testCaseName)
            {
                FixtureNames = new HashSet<string> { fixtureName };
                TestCaseNames = new HashSet<string> { testCaseName };
            }

            public FixtureAndCaseFilter(string fixtureName)
            {
                FixtureNames = new HashSet<string> { fixtureName };
                TestCaseNames = new HashSet<string>();
            }

            bool ITestFilter.IsExplicitMatch(ITest test)
            {
                return false;
            }

            bool ITestFilter.Pass(ITest test)
            {
                if (FixtureNames.Count == 0 || test.IsSuite)
                {
                    return true;
                }
                if (FixtureNames.Contains(test.TypeInfo.Name))
                {
                    if (TestCaseNames.Count == 0)
                    {
                        return true;
                    }
                    else
                    {
                        return TestCaseNames.Contains(test.Name);
                    }
                }
                else
                {
                    return false;
                }
            }
            public TNode ToXml(bool b)
            {
                return null;
            }

            public TNode AddToXml(TNode n, bool b)
            {
                return null;
            }
        }

        #region ITestListener

        public void TestFinished(ITestResult result)
        {
            var testAssembly = result.Test as TestAssembly;
            if (testAssembly != null)
            {
                Debug.Log(string.Format("=== Executed {0} tests in assembly {1} ===",
                    testAssembly.TestCaseCount,
                    testAssembly.Assembly.FullName));

                Debug.Log(string.Format("\nPassed : {0}\tFailed : {1}",
                    result.PassCount, result.FailCount));

            }

            var testFixture = result.Test as TestFixture;
            if (testFixture != null)
            {
                if (result.FailCount > 0)
                {
                    Debug.Log(string.Format("Test Fixture {0} ({1}) has {2} failures.",
                        testFixture.Name, testFixture.FullName, result.FailCount));

                    if (result.HasChildren)
                    {
                        foreach (var childResult in result.Children)
                        {
                            if (childResult.ResultState.Site != FailureSite.Test)
                                TestFinished(childResult);
                        }
                    }

                    Debug.Log(string.Format("\tMessage : {0}", result.Message));
                    Debug.Log(string.Format("\tStack trace : {0}", result.StackTrace));
                }
                Debug.Log(string.Format("  --- Executed tests in class {0}   ---\n", testFixture.FullName));
            }

            var testMethod = result.Test as TestMethod;
            if (testMethod != null)
            {
                if (result.FailCount > 0)
                {
                    Debug.Log(string.Format("FAIL {0} ({1})", testMethod.MethodName, testMethod.FullName));
                    Debug.Log(string.Format("\tMessage : {0}", result.Message));
                    Debug.Log(string.Format("\tStack trace : {0}", result.StackTrace));
                }

                if (result.InconclusiveCount > 0)
                {
                    Debug.Log(string.Format("INCONCLUSIVE {0}", testMethod.MethodName));
                }

                if (result.PassCount > 0)
                {
                    Debug.Log(string.Format("PASS {0}", testMethod.MethodName));
                }

                var testSucceeded =
                    result.FailCount == 0 &&
                    result.InconclusiveCount == 0 &&
                    result.SkipCount == 0;

                TestCompleted(testMethod.Name, testSucceeded);
            }
        }

        public void TestStarted(ITest test)
        {
            var testAssembly = test as TestAssembly;
            if (testAssembly != null)
            {
                Debug.Log(string.Format("=== Executing tests in assembly {0} ===\n",
                    testAssembly.Assembly.FullName));
            }

            var testFixture = test as TestFixture;
            if (testFixture != null)
            {
                Debug.Log(string.Format("  --- Executing tests in class {0} ---",
                    testFixture.FullName));
            }

            var testMethod = test as TestMethod;
            if (testMethod != null)
            {
                Debug.Log(string.Format("\tTest {0}.{1} started", testMethod.ClassName, testMethod.MethodName));
            }
        }
        #endregion

        #region test reporting
        protected virtual void TestCompleted(string testMethodName, bool succeeded)
        {
            var res = string.Format(@"Test '{0}'  {1}", testMethodName, succeeded ? @"PASSED" : @"FAILED");
            TestDriver.Results.Enqueue(res);
        }
        #endregion
    }
}
