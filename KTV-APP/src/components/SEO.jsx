import { useEffect } from 'react';

const SEO = ({ title, description, keywords, schema, canonical }) => {
    useEffect(() => {
        // Update Title
        if (title) {
            document.title = title;
        }

        // Update Meta Description
        let metaDescription = document.querySelector('meta[name="description"]');
        if (!metaDescription) {
            metaDescription = document.createElement('meta');
            metaDescription.setAttribute('name', 'description');
            document.head.appendChild(metaDescription);
        }
        if (description) {
            metaDescription.setAttribute('content', description);
        }

        // Update Meta Keywords
        let metaKeywords = document.querySelector('meta[name="keywords"]');
        if (!metaKeywords) {
            metaKeywords = document.createElement('meta');
            metaKeywords.setAttribute('name', 'keywords');
            document.head.appendChild(metaKeywords);
        }
        if (keywords) {
            metaKeywords.setAttribute('content', keywords);
        }

        // Handle Canonical Link
        let linkCanonical = document.querySelector('link[rel="canonical"]');
        if (!linkCanonical) {
            linkCanonical = document.createElement('link');
            linkCanonical.setAttribute('rel', 'canonical');
            document.head.appendChild(linkCanonical);
        }
        linkCanonical.setAttribute('href', canonical || window.location.href);

        // Handle JSON-LD Schema
        const existingSchema = document.getElementById('json-ld-schema');
        if (existingSchema) {
            existingSchema.remove();
        }

        if (schema) {
            const script = document.createElement('script');
            script.id = 'json-ld-schema';
            script.type = 'application/ld+json';
            script.text = JSON.stringify(schema);
            document.head.appendChild(script);
        }
    }, [title, description, keywords, schema, canonical]);

    return null;
};

export default SEO;
