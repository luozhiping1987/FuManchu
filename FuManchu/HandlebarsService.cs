﻿namespace FuManchu
{
	using System;
	using System.Collections.Concurrent;
	using System.IO;
	using System.Linq;
	using FuManchu.Binding;
	using FuManchu.Parser;
	using FuManchu.Parser.SyntaxTree;
	using FuManchu.Renderer;
	using FuManchu.Tags;
	using FuManchu.Text;

	public class HandlebarsService : IHandlebarsService
	{
		private readonly ConcurrentDictionary<string, Func<RenderContext, string>> _partials = new ConcurrentDictionary<string, Func<RenderContext, string>>();
		private readonly ConcurrentDictionary<string, Func<object, string>> _templates = new ConcurrentDictionary<string, Func<object, string>>(); 

		public HandlebarsService()
		{
			TagProviders = new TagProvidersCollection(TagProvidersCollection.Default);
			ModelMetadataProvider = new DefaultModelMetadataProvider();
		}

		public TagProvidersCollection TagProviders { get; private set; }

		public IModelMetadataProvider ModelMetadataProvider { get; set; }

		public Func<object, string> Compile(string template)
		{
			var document = CreateDocument(template);

			// Collapse any whitespace.
			var whitespace = new WhiteSpaceCollapsingParserVisitor();
			document.Accept(whitespace);

			return (model) =>
			       {
				       using (var writer = new StringWriter())
				       {
					       var render = new RenderingParserVisitor(writer, model, ModelMetadataProvider ?? new DefaultModelMetadataProvider())
					                    {
						                    Service = this
					                    };

					       // Render the document.
					       document.Accept(render);

					       return writer.GetStringBuilder().ToString();
				       }
			       };
		}

		public Func<object, string> Compile(string template, string name)
		{
			Func<object, string> func;
			if (_templates.TryGetValue(name, out func))
			{
				return func;
			}

			func = Compile(template);
			_templates.TryAdd(name, func);

			return func;
		}

		public string CompileAndRun(string template, object model = null, string name = null)
		{
			Func<object, string> func = (string.IsNullOrEmpty(name)) ? Compile(template) : Compile(template, name);

			return func(model);
		}

		public Func<RenderContext, string> CompilePartial(string template, string name)
		{
			var document = CreateDocument(template);

			// Collapse any whitespace.
			var whitespace = new WhiteSpaceCollapsingParserVisitor();
			document.Accept(whitespace);

			return (context) =>
			{
				using (var writer = new StringWriter())
				{
					var render = new RenderingParserVisitor(writer, context, ModelMetadataProvider ?? new DefaultModelMetadataProvider())
					             {
						             Service = this
					             };

					// Render the document.
					document.Accept(render);

					return writer.GetStringBuilder().ToString();
				}
			};
		}

		public void RegisterPartial(string name, Func<RenderContext, string> func)
		{
			Func<RenderContext, string> temp;
			if (!_partials.TryGetValue(name, out temp))
			{
				_partials.TryAdd(name, func);
			}
		}

		public void RegisterPartial(string name, string template)
		{
			Func<RenderContext, string> func;
			if (!_partials.TryGetValue(name, out func))
			{
				func = CompilePartial(template, name);
				_partials.TryAdd(name, func);
			}
		}

		public void RemoveCompiledTemplate(string name)
		{
			Func<object, string> func;
			_templates.TryRemove(name, out func);
		}

		public string Run(string name, object model = null)
		{
			Func<object, string> func;
			if (_templates.TryGetValue(name, out func))
			{
				return func(model);
			}

			throw new ArgumentException("No template called '" + name + "' has been compiled.");
		}

		public string RunPartial(string name, RenderContext context)
		{
			Func<RenderContext, string> func;
			if (_partials.TryGetValue(name, out func))
			{
				return func(context);
			}

			throw new ArgumentException("No partial template called '" + name + "' has been compiled.");
		}

		private Block CreateDocument(string template)
		{
			using (var reader = new StringReader(template))
			{
				using (var source = new SeekableTextReader(reader))
				{
					var errors = new ParserErrorSink();
					var parser = new HandlebarsParser();

					var context = new ParserContext(source, parser, errors, TagProviders);
					parser.Context = context;

					parser.ParseDocument();
					var results = context.CompleteParse();

					if (results.Success)
					{
						return results.Document;
					}

					throw new InvalidOperationException(
						string.Join("\n", results.Errors.Select(e => string.Format("{0}: {1}", e.Location, e.Message))));
				}
			}
		}
	}
}