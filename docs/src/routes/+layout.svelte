<script lang="ts">
	import Footer from '/src/routes/footer.svelte';
	import Header from '/src/routes/header.svelte';

	import 'open-props/style'
	import 'open-props/buttons'

	import '../app.css'
</script>

<div>
  <!-- Header -->
	<Header />

	<main class="container bg-white">
		<slot />
	</main>

  <!-- Footer -->
	<Footer />
</div>
