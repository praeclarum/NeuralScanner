// SDF.cs
//
// This file was automatically generated and should not be edited.
//

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

using CoreML;
using CoreVideo;
using Foundation;

namespace NeuralScanner {
	/// <summary>
	/// Model Prediction Input Type
	/// </summary>
	public class SDFInput : NSObject, IMLFeatureProvider
	{
		static readonly NSSet<NSString> featureNames = new NSSet<NSString> (
			new NSString ("xyz")
		);

		MLMultiArray xyz;

		/// <summary>
		///  as 1 x 3 2-dimensional array of floats
		/// </summary>
		/// <value></value>
		public MLMultiArray Xyz {
			get { return xyz; }
			set {
				if (value == null)
					throw new ArgumentNullException (nameof (value));

				xyz = value;
			}
		}

		public NSSet<NSString> FeatureNames {
			get { return featureNames; }
		}

		public MLFeatureValue GetFeatureValue (string featureName)
		{
			switch (featureName) {
			case "xyz":
				return MLFeatureValue.Create (Xyz);
			default:
				return null;
			}
		}

		public SDFInput (MLMultiArray xyz)
		{
			if (xyz == null)
				throw new ArgumentNullException (nameof (xyz));

			Xyz = xyz;
		}
	}

	/// <summary>
	/// Model Prediction Output Type
	/// </summary>
	public class SDFOutput : NSObject, IMLFeatureProvider
	{
		static readonly NSSet<NSString> featureNames = new NSSet<NSString> (
			new NSString ("Identity")
		);

		MLMultiArray Identity;

		/// <summary>
		///  as  0-dimensional array of floats
		/// </summary>
		/// <value></value>
		public MLMultiArray Identity {
			get { return Identity; }
			set {
				if (value == null)
					throw new ArgumentNullException (nameof (value));

				Identity = value;
			}
		}

		public NSSet<NSString> FeatureNames {
			get { return featureNames; }
		}

		public MLFeatureValue GetFeatureValue (string featureName)
		{
			switch (featureName) {
			case "Identity":
				return MLFeatureValue.Create (Identity);
			default:
				return null;
			}
		}

		public SDFOutput (MLMultiArray Identity)
		{
			if (Identity == null)
				throw new ArgumentNullException (nameof (Identity));

			Identity = Identity;
		}
	}

	/// <summary>
	/// Class for model loading and prediction
	/// </summary>
	public class SDF : NSObject
	{
		readonly MLModel model;

		static NSUrl GetModelUrl ()
		{
			return NSBundle.MainBundle.GetUrlForResource ("SDF", "mlmodelc");
		}

		public SDF ()
		{
			NSError err;

			model = MLModel.Create (GetModelUrl (), out err);
		}

		SDF (MLModel model)
		{
			this.model = model;
		}

		public static SDF Create (NSUrl url, out NSError error)
		{
			if (url == null)
				throw new ArgumentNullException (nameof (url));

			var model = MLModel.Create (url, out error);

			if (model == null)
				return null;

			return new SDF (model);
		}

		public static SDF Create (MLModelConfiguration configuration, out NSError error)
		{
			if (configuration == null)
				throw new ArgumentNullException (nameof (configuration));

			var model = MLModel.Create (GetModelUrl (), configuration, out error);

			if (model == null)
				return null;

			return new SDF (model);
		}

		public static SDF Create (NSUrl url, MLModelConfiguration configuration, out NSError error)
		{
			if (url == null)
				throw new ArgumentNullException (nameof (url));

			if (configuration == null)
				throw new ArgumentNullException (nameof (configuration));

			var model = MLModel.Create (url, configuration, out error);

			if (model == null)
				return null;

			return new SDF (model);
		}

		/// <summary>
		/// Make a prediction using the standard interface
		/// </summary>
		/// <param name="input">an instance of SDFInput to predict from</param>
		/// <param name="error">If an error occurs, upon return contains an NSError object that describes the problem.</param>
		public SDFOutput GetPrediction (SDFInput input, out NSError error)
		{
			if (input == null)
				throw new ArgumentNullException (nameof (input));

			var prediction = model.GetPrediction (input, out error);

			if (prediction == null)
				return null;

			var IdentityValue = prediction.GetFeatureValue ("Identity").MultiArrayValue;

			return new SDFOutput (IdentityValue);
		}

		/// <summary>
		/// Make a prediction using the standard interface
		/// </summary>
		/// <param name="input">an instance of SDFInput to predict from</param>
		/// <param name="options">prediction options</param>
		/// <param name="error">If an error occurs, upon return contains an NSError object that describes the problem.</param>
		public SDFOutput GetPrediction (SDFInput input, MLPredictionOptions options, out NSError error)
		{
			if (input == null)
				throw new ArgumentNullException (nameof (input));

			if (options == null)
				throw new ArgumentNullException (nameof (options));

			var prediction = model.GetPrediction (input, options, out error);

			if (prediction == null)
				return null;

			var IdentityValue = prediction.GetFeatureValue ("Identity").MultiArrayValue;

			return new SDFOutput (IdentityValue);
		}

		/// <summary>
		/// Make a prediction using the convenience interface
		/// </summary>
		/// <param name="xyz"> as 1 x 3 2-dimensional array of floats</param>
		/// <param name="error">If an error occurs, upon return contains an NSError object that describes the problem.</param>
		public SDFOutput GetPrediction (MLMultiArray xyz, out NSError error)
		{
			var input = new SDFInput (xyz);

			return GetPrediction (input, out error);
		}

		/// <summary>
		/// Make a prediction using the convenience interface
		/// </summary>
		/// <param name="xyz"> as 1 x 3 2-dimensional array of floats</param>
		/// <param name="options">prediction options</param>
		/// <param name="error">If an error occurs, upon return contains an NSError object that describes the problem.</param>
		public SDFOutput GetPrediction (MLMultiArray xyz, MLPredictionOptions options, out NSError error)
		{
			var input = new SDFInput (xyz);

			return GetPrediction (input, options, out error);
		}
	}
}
